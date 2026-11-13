using System.Linq;
using Content.Shared._WF.Planets;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Planets;

public sealed partial class WFWorldFormingSystem
{
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>The shortest gap between two sends that only move progress.</summary>
    private static readonly TimeSpan WfProgressInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _wfNextProgress;
    private readonly HashSet<ICommonSession> _wfJoined = new();

    /// <summary>How many times the state went to every player.</summary>
    public int WfBroadcasts { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += WfOnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= WfOnPlayerStatusChanged;
        _wfJoined.Clear();
    }

    private void WfOnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            _wfJoined.Add(args.Session);
        else
            _wfJoined.Remove(args.Session);
    }

    /// <summary>
    /// Sends the state to everyone when the worlds, their stages or the hold changed, or at most once a second when
    /// only progress moved; else sends the last state to players who joined since.
    /// </summary>
    private void WfSendState(WFWorldFormingEvent state)
    {
        var now = _timing.CurTime;
        if (WfStructureChanged(state, Last) || now >= _wfNextProgress && WfProgressChanged(state, Last))
        {
            Last = state;
            _wfNextProgress = now + WfProgressInterval;
            _wfJoined.Clear();
            WfBroadcasts++;
            RaiseNetworkEvent(state);
            return;
        }

        if (_wfJoined.Count == 0)
            return;

        // An empty state is what a fresh client already holds.
        if (Last.Worlds.Count > 0 || Last.RoundHeld)
        {
            var filter = Filter.Empty();
            foreach (var session in _wfJoined)
            {
                if (session.Status == SessionStatus.InGame)
                    filter.AddPlayer(session);
            }

            RaiseNetworkEvent(Last, filter);
        }

        _wfJoined.Clear();
    }

    /// <summary>Whether anything but progress differs between two states.</summary>
    private static bool WfStructureChanged(WFWorldFormingEvent a, WFWorldFormingEvent b)
    {
        if (a.RoundHeld != b.RoundHeld || a.Worlds.Count != b.Worlds.Count)
            return true;

        for (var i = 0; i < a.Worlds.Count; i++)
        {
            var x = a.Worlds[i];
            var y = b.Worlds[i];
            if (x.Ground != y.Ground
                || x.Stage != y.Stage
                || x.Name != y.Name
                || x.RoundStart != y.RoundStart
                || !x.Maps.SequenceEqual(y.Maps))
                return true;
        }

        return false;
    }

    /// <summary>Whether any progress differs between two states of the same worlds.</summary>
    private static bool WfProgressChanged(WFWorldFormingEvent a, WFWorldFormingEvent b)
    {
        // With no worlds the overall is a placeholder.
        if (a.Worlds.Count == 0)
            return false;

        if (!a.Overall.Equals(b.Overall))
            return true;

        for (var i = 0; i < a.Worlds.Count; i++)
        {
            if (!a.Worlds[i].Progress.Equals(b.Worlds[i].Progress))
                return true;
        }

        return false;
    }
}
