using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._Battlefield14.FactionTracking;

public sealed partial class FactionTrackingSystem
{
    private TickerFactionCountEvent? _wfLastSent;
    private readonly HashSet<ICommonSession> _wfJoined = new();

    /// <summary>How many times the counts went to every player.</summary>
    public int WfBroadcasts { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += WfOnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= WfOnPlayerStatusChanged;
        _wfJoined.Clear();
    }

    private void WfOnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.InGame)
            _wfJoined.Add(args.Session);
        else
            _wfJoined.Remove(args.Session);
    }

    /// <summary>Sends the counts to everyone when they changed, else the last ones to players who joined since.</summary>
    private void WfSendCounts(TickerFactionCountEvent ev)
    {
        if (_wfLastSent == null
            || _wfLastSent.IsRoundStarted != ev.IsRoundStarted
            || _wfLastSent.BluforReady != ev.BluforReady
            || _wfLastSent.RedforReady != ev.RedforReady
            || _wfLastSent.TotalReady != ev.TotalReady
            || _wfLastSent.BluforAlive != ev.BluforAlive
            || _wfLastSent.RedforAlive != ev.RedforAlive)
        {
            _wfLastSent = ev;
            _wfJoined.Clear();
            WfBroadcasts++;
            RaiseNetworkEvent(ev);
            return;
        }

        if (_wfJoined.Count == 0)
            return;

        var filter = Filter.Empty();
        foreach (var session in _wfJoined)
        {
            if (session.Status == SessionStatus.InGame)
                filter.AddPlayer(session);
        }

        _wfJoined.Clear();
        RaiseNetworkEvent(ev, filter);
    }
}
