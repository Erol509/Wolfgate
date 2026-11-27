using System.Numerics;
using Content.Shared._WG.BulkCargo;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Server.Decals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.BulkCargo;

/// <summary>What a tile in a delivery zone holds for a crate right now.</summary>
public enum WGCargoCellState : byte
{
    Free,
    Space,
    Blocked,
    Reserved,
}

/// <summary>Two paired markers and the inclusive rectangle between them, in grid-local tiles.</summary>
public readonly record struct WGCargoZone(EntityUid First, EntityUid Second, EntityUid Grid, Guid Id, Vector2i Min, Vector2i Max)
{
    public int Width => Max.X - Min.X + 1;

    public int Height => Max.Y - Min.Y + 1;

    public int Cells => Width * Height;
}

/// <summary>Where one zone stands: how many of its tiles take a crate, and how many are blocked or spoken for.</summary>
public readonly record struct WGCargoZoneCount(int Free, int Blocked, int Space, int Reserved)
{
    public int Usable => Free + Reserved;
}

/// <summary>The tiles of one zone, split by what the preview should draw in each.</summary>
public readonly record struct WGCargoZoneCells(List<Vector2i> Free, List<Vector2i> Blocked, List<Vector2i> Reserved);

/// <summary>Raised when a zone is taken down, so paid orders holding its cells stop and refund at once.</summary>
public readonly record struct WGCargoZoneRemovedEvent(Guid Zone);

/// <summary>
/// Delivery zones built from two placed markers. A zone is a grid-local rectangle of one tile per crate: walls, space
/// and physics fixtures block a tile, and a cell reserved by one order can't be taken by another. Cells are read from
/// the live grid and physics, so a zone that follows a moving ship is re-checked where it stands.
/// </summary>
public sealed partial class WGCargoDeliveryZoneSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Most tiles one zone may cover, so a stray pair of markers can't claim half the station.</summary>
    public const int MaxCells = 256;

    /// <summary>Longest side of a zone, which keeps a thin 1 x 256 rectangle from painting hundreds of decals.</summary>
    public const int MaxSide = 64;

    /// <summary>Most zones one grid may hold, so marker spam can't fill a station with entities and decals.</summary>
    public const int MaxZonesPerGrid = 16;

    /// <summary>Most zones the whole round may hold, so projector spam can't grow the world without bound.</summary>
    public const int MaxZones = 256;

    private static readonly Color ZoneLine = Color.FromHex("#C08A3E");
    private static readonly Color ZoneUnselected = Color.FromHex("#8A6A3E");

    /// <summary>Every complete zone by id: its two corners and the grid they stand on.</summary>
    private readonly Dictionary<Guid, (EntityUid First, EntityUid Second, EntityUid Grid)> _zones = new();

    /// <summary>How many zones each grid holds, for the per-grid cap.</summary>
    private readonly Dictionary<EntityUid, int> _zonesPerGrid = new();

    /// <summary>Decal ids painted for each zone, so closing it removes exactly its own markings.</summary>
    private readonly Dictionary<Guid, List<(EntityUid Grid, uint Id)>> _zoneDecals = new();

    /// <summary>The single networked field entity each zone shows in the world.</summary>
    private readonly Dictionary<Guid, EntityUid> _zoneFields = new();

    private static readonly EntProtoId ZoneField = "WGCargoZoneField";

    /// <summary>Which order holds each reserved cell.</summary>
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), Guid> _reserved = new();

    /// <summary>The cells each order holds, for release.</summary>
    private readonly Dictionary<Guid, (EntityUid Grid, List<Vector2i> Cells)> _held = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<WGCargoZoneCornerComponent, ComponentShutdown>(OnCornerShutdown);
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _reserved.Clear();
        _held.Clear();
        _zoneDecals.Clear();
        _zoneFields.Clear();
        _zones.Clear();
        _zonesPerGrid.Clear();
    }

    private void OnCornerShutdown(Entity<WGCargoZoneCornerComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Zone != Guid.Empty)
            RemoveZone(ent.Comp.Zone);
    }

    /// <summary>
    /// Pairs two placed markers into one zone. False when either is already in a zone, they don't stand on one grid, the
    /// rectangle is larger than <see cref="MaxCells"/>, or the grid already holds <see cref="MaxZonesPerGrid"/> zones.
    /// </summary>
    public bool TryPair(EntityUid first, EntityUid second, out Guid id)
    {
        return TryPair(first, second, out id, out _);
    }

    /// <summary>The same pairing, with a localized reason when it is refused.</summary>
    public bool TryPair(EntityUid first, EntityUid second, out Guid id, out string? error)
    {
        id = Guid.Empty;
        error = "wg-bulk-zone-failed";
        if (first == second
            || !TryComp<WGCargoZoneCornerComponent>(first, out var a)
            || !TryComp<WGCargoZoneCornerComponent>(second, out var b)
            || a.Zone != Guid.Empty
            || b.Zone != Guid.Empty
            || Transform(first).GridUid is not { } grid
            || Transform(second).GridUid is not { } other
            || grid != other
            || TerminatingOrDeleted(grid)
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var tileA = _map.TileIndicesFor(grid, gridComp, Transform(first).Coordinates);
        var tileB = _map.TileIndicesFor(grid, gridComp, Transform(second).Coordinates);
        var width = Math.Abs(tileA.X - tileB.X) + 1;
        var height = Math.Abs(tileA.Y - tileB.Y) + 1;
        if (width > MaxSide || height > MaxSide || (long) width * height > MaxCells)
        {
            error = "wg-bulk-zone-too-large";
            return false;
        }

        var min = Vector2i.ComponentMin(tileA, tileB);
        var max = Vector2i.ComponentMax(tileA, tileB);
        foreach (var existing in ZonesOn(grid))
        {
            if (!Overlaps(min, max, existing.Min, existing.Max))
                continue;

            error = "wg-bulk-zone-overlap";
            return false;
        }

        if (_zones.Count >= MaxZones)
        {
            error = "wg-bulk-zone-limit";
            return false;
        }

        if (_zonesPerGrid.GetValueOrDefault(grid) >= MaxZonesPerGrid)
        {
            error = "wg-bulk-zone-too-many";
            return false;
        }

        id = Guid.NewGuid();
        _zones[id] = (first, second, grid);
        _zonesPerGrid[grid] = _zonesPerGrid.GetValueOrDefault(grid) + 1;
        Place((first, a), grid, gridComp, id);
        Place((second, b), grid, gridComp, id);
        PaintZone(id);
        AlignCorners(id);
        return true;
    }

    private void Place(Entity<WGCargoZoneCornerComponent> corner, EntityUid grid, MapGridComponent gridComp, Guid zone)
    {
        corner.Comp.Zone = zone;
        corner.Comp.Grid = grid;
        corner.Comp.Tile = _map.TileIndicesFor(grid, gridComp, Transform(corner).Coordinates);
    }

    /// <summary>Turns both markers so their one drawn corner faces into the rectangle.</summary>
    private void AlignCorners(Guid id)
    {
        if (!TryZone(id, out var zone))
            return;

        if (TryComp<WGCargoZoneCornerComponent>(zone.First, out var first))
            Rotate(zone.First, CornerAngle(first.Tile, zone));

        if (TryComp<WGCargoZoneCornerComponent>(zone.Second, out var second))
            Rotate(zone.Second, CornerAngle(second.Tile, zone));
    }

    /// <summary>Whether two inclusive tile rectangles share any tile.</summary>
    private static bool Overlaps(Vector2i minA, Vector2i maxA, Vector2i minB, Vector2i maxB)
    {
        return minA.X <= maxB.X
               && maxA.X >= minB.X
               && minA.Y <= maxB.Y
               && maxA.Y >= minB.Y;
    }

    /// <summary>The rotation that turns the north-west marker art into the corner at this tile.</summary>
    private static Angle CornerAngle(Vector2i tile, WGCargoZone zone)
    {
        var west = tile.X == zone.Min.X;
        var south = tile.Y == zone.Min.Y;

        if (west && !south)
            return Angle.Zero;
        if (!west && !south)
            return Angle.FromDegrees(-90);
        if (!west)
            return Angle.FromDegrees(180);

        return Angle.FromDegrees(90);
    }

    private void Rotate(EntityUid corner, Angle angle)
    {
        if (!TerminatingOrDeleted(corner) && TryComp<TransformComponent>(corner, out var xform))
            _transform.SetLocalRotation(corner, angle, xform);
    }

    /// <summary>Takes a corner out of its zone; its partner is left waiting for a new one.</summary>
    public void Unpair(EntityUid corner)
    {
        if (!TryComp<WGCargoZoneCornerComponent>(corner, out var comp) || comp.Zone == Guid.Empty)
            return;

        RemoveZone(comp.Zone);
    }

    /// <summary>
    /// Closes a zone: wipes its markings and field, frees both corners and tells any order holding its cells to stop.
    /// </summary>
    private void RemoveZone(Guid id)
    {
        ClearDecals(id);

        if (!_zones.Remove(id, out var entry))
            return;

        if (_zonesPerGrid.TryGetValue(entry.Grid, out var count))
        {
            if (count <= 1)
                _zonesPerGrid.Remove(entry.Grid);
            else
                _zonesPerGrid[entry.Grid] = count - 1;
        }

        ClearCorner(entry.First, id);
        ClearCorner(entry.Second, id);
        RaiseLocalEvent(new WGCargoZoneRemovedEvent(id));
    }

    private void ClearCorner(EntityUid corner, Guid zone)
    {
        if (TerminatingOrDeleted(corner)
            || !TryComp<WGCargoZoneCornerComponent>(corner, out var comp)
            || comp.Zone != zone)
            return;

        comp.Zone = Guid.Empty;
        comp.Grid = null;
        Rotate(corner, Angle.Zero);
    }

    /// <summary>
    /// Paints the chosen rectangle: warning corners for the two placed markers, grey corners for the other two, grey
    /// lines between them and a faint fill inside.
    /// </summary>
    private void PaintZone(Guid id)
    {
        if (!TryZone(id, out var zone) || !TryComp<MapGridComponent>(zone.Grid, out var gridComp))
            return;

        ClearDecals(id);
        var painted = new List<(EntityUid Grid, uint Id)>(32);
        var selected = new HashSet<Vector2i>();
        if (TryComp<WGCargoZoneCornerComponent>(zone.First, out var first))
            selected.Add(first.Tile);
        if (TryComp<WGCargoZoneCornerComponent>(zone.Second, out var second))
            selected.Add(second.Tile);

        void Add(string decal, int x, int y, Color? color)
        {
            // Decals are drawn from their bottom-left, not the tile centre.
            var coords = _map.GridTileToLocal(zone.Grid, gridComp, new Vector2i(x, y))
                .Offset(new Vector2(-0.5f, -0.5f));
            if (_decals.TryAddDecal(decal, coords, out var decalId, color))
                painted.Add((zone.Grid, decalId));
        }

        var corners = new HashSet<Vector2i>
        {
            new(zone.Min.X, zone.Min.Y),
            new(zone.Min.X, zone.Max.Y),
            new(zone.Max.X, zone.Min.Y),
            new(zone.Max.X, zone.Max.Y),
        };
        foreach (var corner in corners)
        {
            var chosen = selected.Contains(corner);
            Add(CornerDecal(chosen, corner, zone), corner.X, corner.Y, chosen ? null : ZoneUnselected);
        }

        if (zone.Max.X > zone.Min.X)
        {
            for (var x = zone.Min.X + 1; x < zone.Max.X; x++)
            {
                Add("WarnLineGreyscaleN", x, zone.Max.Y, ZoneLine);
                Add("WarnLineGreyscaleS", x, zone.Min.Y, ZoneLine);
            }
        }

        if (zone.Max.Y > zone.Min.Y)
        {
            for (var y = zone.Min.Y + 1; y < zone.Max.Y; y++)
            {
                Add("WarnLineGreyscaleW", zone.Min.X, y, ZoneLine);
                Add("WarnLineGreyscaleE", zone.Max.X, y, ZoneLine);
            }
        }

        _zoneDecals[id] = painted;

        // One pulsing field entity carries the whole rectangle; the client draws it in a single pass.
        var centre = new Vector2i((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);
        var field = Spawn(ZoneField, _map.GridTileToLocal(zone.Grid, gridComp, centre));
        var fieldComp = EnsureComp<WGCargoZoneFieldComponent>(field);
        fieldComp.Min = zone.Min;
        fieldComp.Max = zone.Max;
        Dirty(field, fieldComp);
        _zoneFields[id] = field;
    }

    private static string CornerDecal(bool selected, Vector2i tile, WGCargoZone zone)
    {
        var vertical = tile.Y == zone.Max.Y ? "N" : "S";
        var horizontal = tile.X == zone.Max.X ? "E" : "W";
        return selected
            ? $"WarnCorner{vertical}{horizontal}"
            : $"WarnCornerGreyscale{vertical}{horizontal}";
    }

    /// <summary>Wipes the decals and field painted for a zone, leaving other decals on the grid alone.</summary>
    private void ClearDecals(Guid zone)
    {
        if (_zoneFields.Remove(zone, out var field) && !TerminatingOrDeleted(field))
            QueueDel(field);

        if (!_zoneDecals.Remove(zone, out var decals))
            return;

        foreach (var (grid, decal) in decals)
        {
            if (TerminatingOrDeleted(grid) || !HasComp<DecalGridComponent>(grid))
                continue;

            _decals.RemoveDecal(grid, decal);
        }
    }

    /// <summary>Reads a zone from its two corners, or false while a corner is missing or they no longer share a grid.</summary>
    public bool TryZone(Guid id, out WGCargoZone zone)
    {
        zone = default;
        if (id == Guid.Empty || !_zones.TryGetValue(id, out var entry))
            return false;

        if (!TryComp<WGCargoZoneCornerComponent>(entry.First, out var a)
            || !TryComp<WGCargoZoneCornerComponent>(entry.Second, out var b)
            || a.Zone != id
            || b.Zone != id
            || a.Grid is not { } grid
            || a.Grid != b.Grid
            || Transform(entry.First).GridUid != grid
            || Transform(entry.Second).GridUid != grid
            || TerminatingOrDeleted(grid)
            || !HasComp<MapGridComponent>(grid))
        {
            RemoveZone(id);
            return false;
        }

        var min = Vector2i.ComponentMin(a.Tile, b.Tile);
        var max = Vector2i.ComponentMax(a.Tile, b.Tile);
        var width = max.X - min.X + 1;
        var height = max.Y - min.Y + 1;
        if (width > MaxSide || height > MaxSide || (long) width * height > MaxCells)
        {
            RemoveZone(id);
            return false;
        }

        zone = new WGCargoZone(entry.First, entry.Second, grid, id, min, max);
        return true;
    }

    /// <summary>Every complete zone standing on a grid.</summary>
    public List<WGCargoZone> ZonesOn(EntityUid grid)
    {
        // Collect first: TryZone may close a zone whose corners no longer agree.
        var ids = new List<Guid>();
        foreach (var (id, entry) in _zones)
        {
            if (entry.Grid == grid)
                ids.Add(id);
        }

        var zones = new List<WGCargoZone>(ids.Count);
        foreach (var id in ids)
        {
            if (TryZone(id, out var zone))
                zones.Add(zone);
        }

        return zones;
    }

    /// <summary>Whether one tile takes a crate now, leaving out whatever <paramref name="held"/> already has reserved.</summary>
    public WGCargoCellState Classify(WGCargoZone zone, Vector2i tile, Guid held = default)
    {
        return ClassifyCell(zone.Grid, tile, held);
    }

    /// <summary>Whether one tile on a grid takes a crate now.</summary>
    public WGCargoCellState ClassifyCell(EntityUid grid, Vector2i tile, Guid held = default)
    {
        if (!TryComp<MapGridComponent>(grid, out var gridComp)
            || !_map.TryGetTileRef(grid, gridComp, tile, out var tileRef)
            || tileRef.Tile.IsEmpty)
            return WGCargoCellState.Space;

        if (_turf.IsTileBlocked(grid, tile, CollisionGroup.CrateMask, gridComp))
            return WGCargoCellState.Blocked;

        if (_reserved.TryGetValue((grid, tile), out var holder) && holder != held)
            return WGCargoCellState.Reserved;

        return WGCargoCellState.Free;
    }

    /// <summary>How many of a zone's tiles take a crate now.</summary>
    public int FreeCount(WGCargoZone zone, Guid held = default)
    {
        var free = 0;
        for (var y = zone.Min.Y; y <= zone.Max.Y; y++)
        {
            for (var x = zone.Min.X; x <= zone.Max.X; x++)
            {
                if (Classify(zone, new Vector2i(x, y), held) == WGCargoCellState.Free)
                    free++;
            }
        }

        return free;
    }

    /// <summary>What a zone holds now: free tiles, and walled, space and already-reserved ones.</summary>
    public WGCargoZoneCount Count(WGCargoZone zone)
    {
        var free = 0;
        var blocked = 0;
        var space = 0;
        var reserved = 0;
        for (var y = zone.Min.Y; y <= zone.Max.Y; y++)
        {
            for (var x = zone.Min.X; x <= zone.Max.X; x++)
            {
                switch (Classify(zone, new Vector2i(x, y)))
                {
                    case WGCargoCellState.Free:
                        free++;
                        break;
                    case WGCargoCellState.Blocked:
                        blocked++;
                        break;
                    case WGCargoCellState.Space:
                        space++;
                        break;
                    case WGCargoCellState.Reserved:
                        reserved++;
                        break;
                }
            }
        }

        return new WGCargoZoneCount(free, blocked, space, reserved);
    }

    /// <summary>The zone's tiles split by state, for the delivery preview.</summary>
    public WGCargoZoneCells Describe(WGCargoZone zone)
    {
        var free = new List<Vector2i>();
        var blocked = new List<Vector2i>();
        var reserved = new List<Vector2i>();
        for (var y = zone.Min.Y; y <= zone.Max.Y; y++)
        {
            for (var x = zone.Min.X; x <= zone.Max.X; x++)
            {
                var tile = new Vector2i(x, y);
                switch (Classify(zone, tile))
                {
                    case WGCargoCellState.Free:
                        free.Add(tile);
                        break;
                    case WGCargoCellState.Blocked:
                    case WGCargoCellState.Space:
                        blocked.Add(tile);
                        break;
                    case WGCargoCellState.Reserved:
                        reserved.Add(tile);
                        break;
                }
            }
        }

        return new WGCargoZoneCells(free, blocked, reserved);
    }

    /// <summary>
    /// Reserves the first free tiles of a zone for one order, row by row from its lowest corner. False, with nothing
    /// reserved, if the zone can't hold them all.
    /// </summary>
    public bool TryReserve(Guid order, WGCargoZone zone, int count, out List<Vector2i> cells)
    {
        cells = new List<Vector2i>(Math.Max(count, 0));
        if (count <= 0 || count > zone.Cells)
            return false;

        for (var y = zone.Min.Y; y <= zone.Max.Y && cells.Count < count; y++)
        {
            for (var x = zone.Min.X; x <= zone.Max.X && cells.Count < count; x++)
            {
                var tile = new Vector2i(x, y);
                if (Classify(zone, tile, order) == WGCargoCellState.Free)
                    cells.Add(tile);
            }
        }

        if (cells.Count < count)
        {
            cells.Clear();
            return false;
        }

        _held[order] = (zone.Grid, cells);
        foreach (var tile in cells)
            _reserved[(zone.Grid, tile)] = order;

        return true;
    }

    /// <summary>Releases everything an order still held.</summary>
    public void Release(Guid order)
    {
        if (!_held.Remove(order, out var held))
            return;

        foreach (var tile in held.Cells)
            _reserved.Remove((held.Grid, tile));
    }

    /// <summary>Reserves one tile for an order that already holds any number of others.</summary>
    public bool TryReserveCell(Guid order, EntityUid grid, Vector2i tile)
    {
        if (_reserved.TryGetValue((grid, tile), out var holder) && holder != order)
            return false;

        if (!_held.TryGetValue(order, out var held))
        {
            _reserved[(grid, tile)] = order;
            _held[order] = (grid, new List<Vector2i> { tile });
            return true;
        }

        // An order keeps to one grid for its whole life; never fold another grid's tile into its held list.
        if (held.Grid != grid)
            return false;

        _reserved[(grid, tile)] = order;
        if (!held.Cells.Contains(tile))
            held.Cells.Add(tile);

        return true;
    }

    /// <summary>Releases one tile of an order, leaving its other cells alone.</summary>
    public void ReleaseCell(Guid order, EntityUid grid, Vector2i tile)
    {
        if (_reserved.TryGetValue((grid, tile), out var holder) && holder == order)
            _reserved.Remove((grid, tile));

        if (_held.TryGetValue(order, out var held))
            held.Cells.Remove(tile);
    }

    /// <summary>Whether some order other than <paramref name="order"/> holds a cell.</summary>
    public bool IsReserved(EntityUid grid, Vector2i tile, Guid order = default)
    {
        return _reserved.TryGetValue((grid, tile), out var holder) && holder != order;
    }
}
