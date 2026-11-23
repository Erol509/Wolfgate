using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WG.BulkCargo;
using Content.Shared.Doors.Components;
using Content.Shared.Item;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._WG.BulkCargo;

/// <summary>
/// The delivery tab's live plot: the ship around the chosen zone drawn as simplified hull tiles, the zone boxed in
/// amber, its free tiles faint, and crates filling in as small squares, one per arrival.
/// </summary>
public sealed class WGBulkDeliveryPlot : Control
{
    /// <summary>Tiles of ship drawn around the zone.</summary>
    private const int Margin = 4;

    /// <summary>Pixels kept clear of the plot's edges.</summary>
    private const float Edge = 16f;

    /// <summary>Seconds between rescans of the zone's grid tiles.</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    /// <summary>Side of a crate marker as a share of its tile.</summary>
    private const float CrateScale = 0.62f;

    private const int MaxDrawVertices = 30000;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedMapSystem _map;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedTransformSystem _transforms;
    private readonly HashSet<EntityUid> _found = new();
    private readonly HashSet<Vector2i> _landed = new();
    private readonly HashSet<Vector2i> _mine = new();
    private Font _small = default!;
    private Font _title = default!;
    private bool _fontsReady;

    private WGCargoBulkZone? _zone;
    private WGCargoBulkOrderEntry? _order;
    private TimeSpan _nextScan;

    private Vector2 _viewMin;
    private Vector2 _viewMax;
    private float _scale;
    private Vector2 _origin;

    private readonly List<Vector2i> _tiles = new();
    private readonly HashSet<Vector2i> _tileSet = new();
    private readonly Dictionary<Vector2i, bool> _walls = new();
    private readonly List<Vector2> _fill = new();
    private readonly List<Vector2> _edges = new();
    private Vector2[] _vertices = Array.Empty<Vector2>();
    private readonly Vector2[] _quad = new Vector2[4];

    public WGBulkDeliveryPlot()
    {
        IoCManager.InjectDependencies(this);
        _map = _entMan.System<SharedMapSystem>();
        _lookup = _entMan.System<EntityLookupSystem>();
        _transforms = _entMan.System<SharedTransformSystem>();
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
        MinSize = new Vector2(360, 300);
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    /// <summary>Takes the selected zone and, when the viewer has one there, its order.</summary>
    public void SetZone(WGCargoBulkZone? zone, WGCargoBulkOrderEntry? order)
    {
        var gridChanged = zone?.Id != _zone?.Id || zone?.Grid != _zone?.Grid;
        _zone = zone;
        _order = order;

        _landed.Clear();
        if (zone != null)
        {
            foreach (var tile in zone.Landed)
                _landed.Add(tile);
        }

        _mine.Clear();
        if (order != null)
        {
            foreach (var tile in order.Cells)
                _mine.Add(tile);
        }

        if (gridChanged)
        {
            _nextScan = TimeSpan.Zero;
            _fill.Clear();
            _edges.Clear();
            _walls.Clear();
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        LoadFonts();
        var time = (float) _timing.RealTime.TotalSeconds;

        handle.DrawRect(new UIBox2(0, 0, PixelWidth, PixelHeight), WGCargoBulkSkin.Base);

        if (PixelWidth < 8 || PixelHeight < 8)
            return;

        if (_zone is not { } zone)
        {
            DrawIdleGrid(handle);
            var blink = MathF.Sin(time * 3f) > -0.2f;
            if (blink)
                DrawCentred(handle, Loc.GetString("wg-bulk-preview-empty"));
            DrawSweep(handle, time);
            return;
        }

        Frame(zone);
        if (_scale <= 0f)
        {
            DrawSweep(handle, time);
            return;
        }

        if (_timing.RealTime >= _nextScan)
        {
            _nextScan = _timing.RealTime + ScanInterval;
            Scan(zone);
        }

        DrawFramedGrid(handle);
        DrawPoints(handle, DrawPrimitiveTopology.TriangleList, _fill, Color.FromHex("#201E18").WithAlpha(0.55f));
        DrawPoints(handle, DrawPrimitiveTopology.LineList, _edges, WGCargoBulkSkin.Accent.WithAlpha(0.35f));
        DrawWalls(handle);
        DrawZoneBox(handle, zone);
        DrawCells(handle, zone, time);
        DrawReadout(handle, zone);
        DrawSweep(handle, time);
    }

    private void LoadFonts()
    {
        if (_fontsReady)
            return;

        _fontsReady = true;
        _small = WGCargoBulkSkin.Sans(_cache, 10);
        _title = WGCargoBulkSkin.Display(_cache, 11);
    }

    /// <summary>Fits the zone, or the whole ship for a ship zone, into the control.</summary>
    private void Frame(WGCargoBulkZone zone)
    {
        var min = new Vector2(zone.Min.X - Margin, zone.Min.Y - Margin);
        var max = new Vector2(zone.Max.X + 1 + Margin, zone.Max.Y + 1 + Margin);

        if (zone.Ship
            && _entMan.TryGetEntity(zone.Grid, out var gridUid)
            && _entMan.TryGetComponent<MapGridComponent>(gridUid.Value, out var grid))
        {
            var ship = grid.LocalAABB;
            min = ship.BottomLeft - new Vector2(2);
            max = ship.TopRight + new Vector2(2);

            // Very large hulls are framed around the bay rather than shrunk to nothing.
            var shipSize = max - min;
            if (shipSize.X > 90 || shipSize.Y > 90)
            {
                var centre = new Vector2((zone.Min.X + zone.Max.X + 1) / 2f, (zone.Min.Y + zone.Max.Y + 1) / 2f);
                var half = new Vector2(Math.Clamp(shipSize.X / 2f, 18f, 45f), Math.Clamp(shipSize.Y / 2f, 18f, 45f));
                min = centre - half;
                max = centre + half;
            }
        }

        var padding = Edge * UIScale;
        _viewMin = min;
        _viewMax = max;
        var size = _viewMax - _viewMin;
        _scale = MathF.Min((PixelWidth - padding * 2) / size.X, (PixelHeight - padding * 2) / size.Y);
        var middle = new Vector2(
            (PixelWidth - size.X * _scale) / 2,
            (PixelHeight - size.Y * _scale) / 2);

        // Keep the grid's Y running up the screen: the top-left of the view is (min.X, max.Y).
        _origin = middle + new Vector2(-_viewMin.X, _viewMax.Y) * _scale;
    }

    private Vector2 Screen(Vector2 point)
    {
        return _origin + new Vector2(point.X, -point.Y) * _scale;
    }

    /// <summary>Rescans the ship's tiles around the zone, then builds their fill and outline.</summary>
    private void Scan(WGCargoBulkZone zone)
    {
        _fill.Clear();
        _edges.Clear();
        _tiles.Clear();

        if (!_entMan.TryGetEntity(zone.Grid, out var gridUid) || !_entMan.TryGetComponent<MapGridComponent>(gridUid.Value, out var grid))
            return;

        var box = new Box2(_viewMin, _viewMax);
        foreach (var tile in _map.GetLocalTilesIntersecting(gridUid.Value, grid, box))
            _tiles.Add(tile.GridIndices);

        _tileSet.Clear();
        foreach (var tile in _tiles)
            _tileSet.Add(tile);

        foreach (var tile in _tiles)
        {
            var a = new Vector2(tile.X, tile.Y);
            var b = a + new Vector2(1, 0);
            var c = a + new Vector2(1, 1);
            var d = a + new Vector2(0, 1);
            _fill.Add(a);
            _fill.Add(b);
            _fill.Add(c);
            _fill.Add(a);
            _fill.Add(c);
            _fill.Add(d);

            if (!_tileSet.Contains(tile + new Vector2i(0, -1)))
            {
                _edges.Add(a);
                _edges.Add(b);
            }
            if (!_tileSet.Contains(tile + new Vector2i(1, 0)))
            {
                _edges.Add(b);
                _edges.Add(c);
            }
            if (!_tileSet.Contains(tile + new Vector2i(0, 1)))
            {
                _edges.Add(c);
                _edges.Add(d);
            }
            if (!_tileSet.Contains(tile + new Vector2i(-1, 0)))
            {
                _edges.Add(d);
                _edges.Add(a);
            }
        }

        // Rooms: walls, doors and anchored machines drawn as the hull's line-work.
        _walls.Clear();
        var minX = (int) MathF.Floor(_viewMin.X);
        var maxX = (int) MathF.Ceiling(_viewMax.X);
        var minY = (int) MathF.Floor(_viewMin.Y);
        var maxY = (int) MathF.Ceiling(_viewMax.Y);

        // Ask the grid's broadphase for statics in the view instead of walking every fixture on the client.
        var world = _transforms.GetWorldMatrix(gridUid.Value).TransformBox(new Box2(_viewMin, _viewMax));
        _found.Clear();
        _lookup.GetEntitiesIntersecting(gridUid.Value, world, _found, LookupFlags.Static);

        foreach (var uid in _found)
        {
            if (!_entMan.TryGetComponent(uid, out FixturesComponent? fixtures)
                || !_entMan.TryGetComponent(uid, out TransformComponent? xform)
                || xform.GridUid != gridUid.Value
                || !xform.Anchored
                || _entMan.HasComponent<ItemComponent>(uid))
                continue;

            var tile = _map.TileIndicesFor(gridUid.Value, grid, xform.Coordinates);
            if (tile.X < minX || tile.X > maxX || tile.Y < minY || tile.Y > maxY)
                continue;

            var structural = false;
            var wallBits = (int) (CollisionGroup.Impassable | CollisionGroup.HighImpassable);
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & wallBits) != 0)
                {
                    structural = true;
                    break;
                }
            }

            if (!structural)
                continue;

            _walls[tile] = _entMan.HasComponent<DoorComponent>(uid);
        }
    }

    private void DrawPoints(DrawingHandleScreen handle, DrawPrimitiveTopology topology, List<Vector2> points, Color color)
    {
        if (points.Count == 0)
            return;

        if (_vertices.Length < points.Count)
            _vertices = new Vector2[points.Count];

        for (var i = 0; i < points.Count; i++)
            _vertices[i] = Screen(points[i]);

        for (var start = 0; start < points.Count; start += MaxDrawVertices)
        {
            var length = Math.Min(MaxDrawVertices, points.Count - start);
            handle.DrawPrimitives(topology, new ReadOnlySpan<Vector2>(_vertices, start, length), color);
        }
    }

    private void DrawFramedGrid(DrawingHandleScreen handle)
    {
        var fine = _scale >= 8 * UIScale ? WGCargoBulkSkin.Accent.WithAlpha(0.035f) : Color.Transparent;
        var coarse = WGCargoBulkSkin.Accent.WithAlpha(0.08f);

        for (var x = (int) MathF.Floor(_viewMin.X); x <= MathF.Ceiling(_viewMax.X); x++)
        {
            var px = MathF.Round(Screen(new Vector2(x, 0)).X);
            if (px < 0 || px > PixelWidth)
                continue;

            var color = Math.Abs(x % 5) == 0 ? coarse : fine;
            if (color.A > 0)
                handle.DrawLine(new Vector2(px, 0), new Vector2(px, PixelHeight), color);
        }

        for (var y = (int) MathF.Floor(_viewMin.Y); y <= MathF.Ceiling(_viewMax.Y); y++)
        {
            var py = MathF.Round(Screen(new Vector2(0, y)).Y);
            if (py < 0 || py > PixelHeight)
                continue;

            var color = Math.Abs(y % 5) == 0 ? coarse : fine;
            if (color.A > 0)
                handle.DrawLine(new Vector2(0, py), new Vector2(PixelWidth, py), color);
        }
    }

    private void DrawIdleGrid(DrawingHandleScreen handle)
    {
        var step = 18 * UIScale;
        var center = PixelSizeBox.Center;
        var fine = WGCargoBulkSkin.Accent.WithAlpha(0.03f);
        var coarse = WGCargoBulkSkin.Accent.WithAlpha(0.075f);

        var startX = center.X - MathF.Ceiling(center.X / step) * step;
        var column = (int) MathF.Round((startX - center.X) / step);
        for (var x = startX; x <= PixelWidth; x += step, column++)
        {
            if (column % 5 == 0 || fine.A > 0)
                handle.DrawLine(new Vector2(x, 0), new Vector2(x, PixelHeight), column % 5 == 0 ? coarse : fine);
        }

        var startY = center.Y - MathF.Ceiling(center.Y / step) * step;
        var row = (int) MathF.Round((startY - center.Y) / step);
        for (var y = startY; y <= PixelHeight; y += step, row++)
        {
            if (row % 5 == 0 || fine.A > 0)
                handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), row % 5 == 0 ? coarse : fine);
        }
    }

    /// <summary>Draws the hull's walls, doors and anchored machinery as orange line-work over the floor.</summary>
    private void DrawWalls(DrawingHandleScreen handle)
    {
        var half = new Vector2(_scale / 2);
        foreach (var (tile, door) in _walls)
        {
            var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            var box = new UIBox2(at - half, at + half);
            var color = door ? WGCargoBulkSkin.Caution : WGCargoBulkSkin.Accent;
            handle.DrawRect(box, color.WithAlpha(door ? 0.18f : 0.08f));
            handle.DrawRect(box, color.WithAlpha(door ? 0.95f : 0.7f), false);
        }
    }

    private void DrawZoneBox(DrawingHandleScreen handle, WGCargoBulkZone zone)    {
        var a = Screen(new Vector2(zone.Min.X, zone.Min.Y));
        var b = Screen(new Vector2(zone.Max.X + 1, zone.Max.Y + 1));
        var box = new UIBox2(Vector2.Min(a, b), Vector2.Max(a, b));
        var dash = MathF.Max(4 * UIScale, _scale * 0.8f);
        var color = WGCargoBulkSkin.Accent.WithAlpha(0.7f);
        Dashed(handle, box.TopLeft, box.TopRight, dash, color);
        Dashed(handle, box.TopRight, box.BottomRight, dash, color);
        Dashed(handle, box.BottomRight, box.BottomLeft, dash, color);
        Dashed(handle, box.BottomLeft, box.TopLeft, dash, color);

        var length = MathF.Min(MathF.Min(box.Width, box.Height) / 4f, 3f * _scale);
        var width = 2 * UIScale;
        void Bracket(Vector2 corner, float dx, float dy)
        {
            Thick(handle, corner, corner + new Vector2(dx * length, 0), width, WGCargoBulkSkin.Accent);
            Thick(handle, corner, corner + new Vector2(0, dy * length), width, WGCargoBulkSkin.Accent);
        }

        Bracket(box.TopLeft, 1f, 1f);
        Bracket(box.TopRight, -1f, 1f);
        Bracket(box.BottomRight, -1f, -1f);
        Bracket(box.BottomLeft, 1f, -1f);

        // Keep the name clear of the bay itself.
        handle.DrawString(_title, new Vector2(8 * UIScale, 6 * UIScale), zone.Label.ToUpperInvariant(),
            UIScale, WGCargoBulkSkin.Accent);
    }

    /// <summary>Free tiles faint, blocked ones crossed, landed crates filled, and the order's crates filling in.</summary>
    private void DrawCells(DrawingHandleScreen handle, WGCargoBulkZone zone, float time)
    {
        var cell = _scale;
        var size = cell * CrateScale;
        var half = new Vector2(size / 2);

        foreach (var tile in zone.Free)
        {
            if (_landed.Contains(tile))
                continue;

            var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            handle.DrawRect(new UIBox2(at - half * 0.5f, at + half * 0.5f), WGCargoBulkSkin.TextDim.WithAlpha(0.18f));
        }

        foreach (var tile in zone.Reserved)
        {
            if (_landed.Contains(tile) || _mine.Contains(tile))
                continue;

            var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            handle.DrawRect(new UIBox2(at - half, at + half), WGCargoBulkSkin.Caution.WithAlpha(0.55f), false);
        }

        foreach (var tile in zone.Blocked)
        {
            if (_landed.Contains(tile) || _mine.Contains(tile))
                continue;

            var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            var r = size * 0.32f;
            var color = WGCargoBulkSkin.Bad.WithAlpha(0.55f);
            handle.DrawLine(at - new Vector2(r, r), at + new Vector2(r, r), color);
            handle.DrawLine(at - new Vector2(r, -r), at + new Vector2(-r, r), color);
        }

        // Crates already delivered stay drawn whether or not their order is still running.
        foreach (var tile in zone.Landed)
        {
            var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
            var box = new UIBox2(at - half, at + half);
            var latest = _order is { } order && order.Delivered > 0
                         && order.Delivered <= order.Cells.Count
                         && order.Cells[order.Delivered - 1] == tile;
            var pulse = latest ? 0.8f + 0.2f * MathF.Sin(time * 6f) : 1f;
            handle.DrawRect(box, WGCargoBulkSkin.Good.WithAlpha(0.2f * pulse));
            handle.DrawRect(box, WGCargoBulkSkin.Good.WithAlpha(0.9f * pulse), false);
        }

        if (_order is not { } paidOrder)
            return;

        for (var i = 0; i < paidOrder.Cells.Count; i++)
        {
            var tile = paidOrder.Cells[i];
            if (_landed.Contains(tile))
                continue;

            if (i >= paidOrder.Delivered)
            {
                var at = Screen(new Vector2(tile.X + 0.5f, tile.Y + 0.5f));
                handle.DrawRect(new UIBox2(at - half, at + half), WGCargoBulkSkin.Accent.WithAlpha(0.5f), false);
            }
        }
    }

    private void DrawReadout(DrawingHandleScreen handle, WGCargoBulkZone zone)
    {
        var cells = (zone.Max.X - zone.Min.X + 1) * (zone.Max.Y - zone.Min.Y + 1);
        var left = Loc.GetString("wg-bulk-preview-space-count", ("count", cells));
        var at = new Vector2(6 * UIScale, PixelHeight - 8 * UIScale - _small.GetHeight(UIScale));
        handle.DrawString(_small, at, left.ToUpperInvariant(), UIScale, WGCargoBulkSkin.TextMuted);

        if (_order is not { } order)
            return;

        var right = Loc.GetString("wg-bulk-preview-transit-count",
            ("remaining", Math.Max(0, order.Total - order.Delivered)), ("total", order.Total));
        var width = handle.GetDimensions(_small, right, UIScale).X;
        handle.DrawString(_small,
            new Vector2(PixelWidth - width - 6 * UIScale, PixelHeight - 8 * UIScale - _small.GetHeight(UIScale)),
            right.ToUpperInvariant(), UIScale, WGCargoBulkSkin.Accent);
    }

    private void DrawCentred(DrawingHandleScreen handle, string text)
    {
        var width = handle.GetDimensions(_small, text, UIScale).X;
        var at = new Vector2((PixelWidth - width) / 2, (PixelHeight - _small.GetHeight(UIScale)) / 2);
        handle.DrawString(_small, at, text.ToUpperInvariant(), UIScale, WGCargoBulkSkin.Accent);
    }

    private void DrawSweep(DrawingHandleScreen handle, float time)
    {
        var y = time / 5f % 1f * (PixelHeight + 40 * UIScale) - 20 * UIScale;
        handle.DrawRect(new UIBox2(0, y - 18 * UIScale, PixelWidth, y), WGCargoBulkSkin.Accent.WithAlpha(0.025f));
        handle.DrawLine(new Vector2(0, y), new Vector2(PixelWidth, y), WGCargoBulkSkin.Accent.WithAlpha(0.09f));
    }

    private void Thick(DrawingHandleScreen handle, Vector2 a, Vector2 b, float width, Color color)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 0.01f)
            return;

        var n = new Vector2(-d.Y, d.X) / length * (width / 2);
        _quad[0] = a + n;
        _quad[1] = a - n;
        _quad[2] = b + n;
        _quad[3] = b - n;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _quad, color);
    }

    private static void Dashed(DrawingHandleScreen handle, Vector2 a, Vector2 b, float dash, Color color)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 0.01f)
            return;

        var unit = d / length;
        for (var at = 0f; at < length; at += dash * 2)
            handle.DrawLine(a + unit * at, a + unit * MathF.Min(at + dash, length), color);
    }
}
