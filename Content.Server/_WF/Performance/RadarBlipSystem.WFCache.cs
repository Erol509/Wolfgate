using Content.Server._WF.Performance;
using Content.Shared._WF.Performance;
using System.Numerics;
using Content.Shared._Mono.Radar;
using Content.Server.GameTicking;

using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Radar;

public sealed partial class RadarBlipSystem
{
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>An enabled blip and where it is this tick.</summary>
    public readonly record struct WFBlip(EntityUid Uid, RadarBlipComponent Blip, TransformComponent Xform, PhysicsComponent Physics, Vector2 Position);

    /// <summary>Side of a square, in metres. Most blips are seen from several of these away.</summary>
    private const float WFCellSize = 1024f;

    /// <summary>The blips in one square of a map, and the furthest away any of them is seen from.</summary>
    private sealed class WFCell
    {
        public MapId Map;
        public Box2 Bounds;
        public float MaxRange;
        public readonly List<WFBlip> Blips = new();
    }

    private readonly Dictionary<(MapId, Vector2i), WFCell> _wfCellIndex = new();
    private readonly List<WFCell> _wfCells = new();
    private int _wfCellsUsed;
    private readonly List<WFBlip> _wfInReach = new();
    private readonly List<Vector2> _wfSourcePositions = new();
    private GameTick _wfBlipsTick = GameTick.Zero;

    /// <summary>Sorts the enabled blips into squares, once a tick however many radars ask.</summary>
    private void WFRefreshBlips()
    {
        if (_wfBlipsTick == _wfTiming.CurTick)
            return;

        _wfBlipsTick = _wfTiming.CurTick;
        _wfCellIndex.Clear();
        for (var i = 0; i < _wfCellsUsed; i++)
        {
            _wfCells[i].Blips.Clear();
        }

        _wfCellsUsed = 0;

        var query = EntityQueryEnumerator<RadarBlipComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var blip, out var xform, out var physics))
        {
            if (!blip.Enabled)
                continue;

            var position = _xform.GetWorldPosition(xform);
            var square = new Vector2i((int) MathF.Floor(position.X / WFCellSize), (int) MathF.Floor(position.Y / WFCellSize));
            if (!_wfCellIndex.TryGetValue((xform.MapID, square), out var cell))
            {
                if (_wfCellsUsed == _wfCells.Count)
                    _wfCells.Add(new WFCell());

                cell = _wfCells[_wfCellsUsed++];
                cell.Map = xform.MapID;
                var corner = new Vector2(square.X, square.Y) * WFCellSize;
                cell.Bounds = new Box2(corner, corner + new Vector2(WFCellSize, WFCellSize));
                cell.MaxRange = 0f;
                _wfCellIndex[(xform.MapID, square)] = cell;
            }

            cell.MaxRange = MathF.Max(cell.MaxRange, blip.MaxDistance);
            cell.Blips.Add(new WFBlip(uid, blip, xform, physics, position));
        }
    }

    /// <summary>The enabled blips on a map this tick.</summary>
    public List<WFBlip> WFBlipsOnMap(MapId map)
    {
        WFRefreshBlips();
        var blips = new List<WFBlip>();
        for (var i = 0; i < _wfCellsUsed; i++)
        {
            if (_wfCells[i].Map == map)
                blips.AddRange(_wfCells[i].Blips);
        }

        return blips;
    }

    /// <summary>
    /// The blips on a map that are seen from any of the sources. Squares out of every source's reach are skipped
    /// whole, so a request costs the blips near it, not every blip on the map. The list is reused by the next call.
    /// </summary>
    public List<WFBlip> WFBlipsInReach(MapId map, List<EntityUid> sources)
    {
        WFRefreshBlips();
        WFNoteSources(sources);
        _wfInReach.Clear();
        for (var i = 0; i < _wfCellsUsed; i++)
        {
            var cell = _wfCells[i];
            if (cell.Map != map || !WFCellInReach(cell))
                continue;

            foreach (var blip in cell.Blips)
            {
                if (WFNearAnySource(blip.Position, blip.Blip.MaxDistance))
                    _wfInReach.Add(blip);
            }
        }

        return _wfInReach;
    }

    /// <summary>Whether any source is close enough to the square to see its furthest-seen blip.</summary>
    private bool WFCellInReach(WFCell cell)
    {
        var squared = cell.MaxRange * cell.MaxRange;
        foreach (var source in _wfSourcePositions)
        {
            var nearest = Vector2.Clamp(source, cell.Bounds.BottomLeft, cell.Bounds.TopRight);
            if ((source - nearest).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    /// <summary>Takes the radar's source positions once, instead of once per blip.</summary>
    private void WFNoteSources(List<EntityUid> sources)
    {
        _wfSourcePositions.Clear();
        foreach (var source in sources)
        {
            _wfSourcePositions.Add(_xform.GetWorldPosition(source));
        }
    }

    private bool WFNearAnySource(Vector2 position, float range)
    {
        var squared = range * range;
        foreach (var source in _wfSourcePositions)
        {
            if ((source - position).LengthSquared() < squared)
                return true;
        }

        return false;
    }

    private static readonly TimeSpan WFRequestWindow = TimeSpan.FromSeconds(3);

    [Dependency] private GameTicker _wfTicker = default!;

    [Dependency] private IConfigurationManager _wfCfg = default!;

    private readonly Dictionary<ICommonSession, TimeSpan> _wfLastRequest = new();
    private readonly Dictionary<ICommonSession, TimeSpan> _wfNextAnswer = new();
    private readonly List<ICommonSession> _wfStale = new();

    /// <summary>Records that a session is looking at a radar.</summary>
    private void WFNoteRequest(ICommonSession session)
    {
        _wfLastRequest[session] = _wfTiming.CurTime;
    }

    /// <summary>
    /// Whether to answer a session's request now: no more than <see cref="WFRadarCVars.MaxRequestHz"/> a second, in
    /// real time so a slow tick doesn't count against the player. The client's own throttle can't be relied on.
    /// </summary>
    public bool WFTakeRequest(ICommonSession session)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(_wfCfg.GetCVar(WFRadarCVars.MaxRequestHz), 0.5f, 30f));
        TimeSpan? due = _wfNextAnswer.TryGetValue(session, out var at) ? at : null;
        // Requests are handled on tick boundaries, so one may land up to a tick early.
        if (!WFRequestCap.Take(ref due, _wfTiming.RealTime, interval, _wfTiming.TickPeriod))
            return false;

        _wfNextAnswer[session] = due.GetValueOrDefault();
        return true;
    }

    private readonly List<NetEntity> _wfRemoved = new();

    /// <summary>Notes a blip that went away; recent radar users are told of the tick's removals together.</summary>
    private void WFSendRemoval(NetEntity blip)
    {
        if (_wfTicker.RunLevel == GameRunLevel.PreRoundLobby || _wfLastRequest.Count == 0)
            return;

        _wfRemoved.Add(blip);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wfRemoved.Count == 0)
            return;

        WFFlushRemovals();
        _wfRemoved.Clear();
    }

    /// <summary>Tells recent radar users which blips are gone; nobody else is drawing them.</summary>
    private void WFFlushRemovals()
    {

        var now = _wfTiming.CurTime;
        var filter = Filter.Empty();
        var any = false;
        _wfStale.Clear();
        foreach (var (session, last) in _wfLastRequest)
        {
            if (now - last > WFRequestWindow || session.Status == SessionStatus.Disconnected)
            {
                _wfStale.Add(session);
                continue;
            }

            filter.AddPlayer(session);
            any = true;
        }

        foreach (var session in _wfStale)
        {
            _wfLastRequest.Remove(session);
            _wfNextAnswer.Remove(session);
        }

        if (any)
            RaiseNetworkEvent(new WFBlipRemovalBatchEvent(new List<NetEntity>(_wfRemoved)), filter);
    }
}
