using System.Numerics;
using Content.Shared._WF.Encounters;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterMarkerSystem
{
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>How far a ship may stray from where clients extrapolate it before the list is sent again, in metres.</summary>
    private const float WfPositionTolerance = 1f;

    /// <summary>How much a ship's velocity may change before the list is sent again, in metres per second.</summary>
    private const float WfVelocityTolerance = 0.1f;

    /// <summary>Matches the client's cap on how long it moves a marker on by its velocity.</summary>
    private const float WfMaxExtrapolation = 10f;

    private List<WFEncounterMarker> _wfLastSent = new();
    private TimeSpan _wfLastSentAt;
    private readonly HashSet<ICommonSession> _wfJoined = new();

    /// <summary>How many times the markers went to every player.</summary>
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
    /// Sends the markers to everyone when they differ from what clients already show, else only to players who joined
    /// since the last update.
    /// </summary>
    private void WfSendMarkers(WFEncounterMarkersEvent ev)
    {
        var now = _timing.CurTime;
        if (WfChanged(ev.Markers, now))
        {
            _wfLastSent = ev.Markers;
            _wfLastSentAt = now;
            _wfJoined.Clear();
            WfBroadcasts++;
            RaiseNetworkEvent(ev, Filter.Broadcast());
            return;
        }

        if (_wfJoined.Count == 0)
            return;

        // An empty list is what a fresh client already holds.
        if (ev.Markers.Count > 0)
        {
            var filter = Filter.Empty();
            foreach (var session in _wfJoined)
            {
                if (session.Status == SessionStatus.InGame)
                    filter.AddPlayer(session);
            }

            RaiseNetworkEvent(ev, filter);
        }

        _wfJoined.Clear();
    }

    /// <summary>Whether the markers differ from the last ones sent by more than client extrapolation covers.</summary>
    private bool WfChanged(List<WFEncounterMarker> markers, TimeSpan now)
    {
        if (markers.Count != _wfLastSent.Count)
            return true;

        var elapsed = MathF.Min((float) (now - _wfLastSentAt).TotalSeconds, WfMaxExtrapolation);
        for (var i = 0; i < markers.Count; i++)
        {
            var a = markers[i];
            var b = _wfLastSent[i];
            if (a.Encounter != b.Encounter
                || a.Grid != b.Grid
                || a.Map != b.Map
                || a.Name != b.Name
                || a.Ship != b.Ship
                || a.Side != b.Side
                || a.Category != b.Category
                || a.Icon != b.Icon
                || a.Color != b.Color
                || a.WarnRange != b.WarnRange
                || a.AttackRange != b.AttackRange
                || a.Threat != b.Threat
                || a.Retrofitted != b.Retrofitted)
                return true;

            if (Vector2.Distance(a.Velocity, b.Velocity) > WfVelocityTolerance)
                return true;

            // The client stops extrapolating here, so a moving marker would freeze until it drifts out of tolerance.
            if (elapsed >= WfMaxExtrapolation && b.Velocity != Vector2.Zero)
                return true;

            var predicted = b.Position + b.Velocity * elapsed;
            if (Vector2.Distance(a.Position, predicted) > WfPositionTolerance)
                return true;
        }

        return false;
    }
}
