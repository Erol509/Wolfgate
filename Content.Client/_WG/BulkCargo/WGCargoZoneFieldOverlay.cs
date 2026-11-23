using System;
using System.Numerics;
using Content.Shared._WG.BulkCargo;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._WG.BulkCargo;

/// <summary>
/// Draws every delivery zone's pass-through energy field: one translucent, slowly pulsing and rippling pass per zone,
/// transformed with its grid so it follows a moving ship. No per-tile entities or sprites are involved.
/// </summary>
public sealed class WGCargoZoneFieldOverlay : Overlay
{
    /// <summary>Most segments a wave band is drawn with, so a huge bay still costs a fixed amount of geometry.</summary>
    private const int MaxBandSegments = 96;

    /// <summary>Reused vertex buffer, so a frame with the field on screen allocates nothing.</summary>
    private Vector2[] _vertices = new Vector2[1024];

    private readonly IEntityManager _entMan;
    private readonly SharedTransformSystem _transforms;
    private readonly IGameTiming _timing;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public WGCargoZoneFieldOverlay(IEntityManager entMan, IGameTiming timing)
    {
        _entMan = entMan;
        _transforms = entMan.System<SharedTransformSystem>();
        _timing = timing;
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var time = (float) _timing.RealTime.TotalSeconds;
        var handle = args.WorldHandle;
        var query = _entMan.EntityQueryEnumerator<WGCargoZoneFieldComponent, TransformComponent>();

        while (query.MoveNext(out _, out var field, out var xform))
        {
            if (xform.MapID != args.MapId
                || xform.GridUid is not { } grid
                || !_entMan.TryGetComponent<TransformComponent>(grid, out var gridXform))
                continue;

            var matrix = _transforms.GetWorldMatrix(gridXform);
            var min = new Vector2(field.Min.X, field.Min.Y);
            var max = new Vector2(field.Max.X + 1, field.Max.Y + 1);

            var a = Vector2.Transform(min, matrix);
            var b = Vector2.Transform(new Vector2(max.X, min.Y), matrix);
            var c = Vector2.Transform(max, matrix);
            var d = Vector2.Transform(new Vector2(min.X, max.Y), matrix);
            var bounds = new Box2(
                Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)),
                Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
            if (!args.WorldAABB.Intersects(bounds))
                continue;

            handle.SetTransform(matrix);
            DrawField(handle, min, max, time);
            handle.SetTransform(Matrix3x2.Identity);
        }
    }

    /// <summary>One fill, a handful of slow wave bands and a faint border, all batched into a few draw calls.</summary>
    private void DrawField(DrawingHandleWorld handle, Vector2 min, Vector2 max, float time)
    {
        var pulse = 0.78f + 0.22f * MathF.Sin(time * 1.25f);
        var orange = new Color(1f, 0.64f, 0.27f);

        handle.DrawRect(new Box2(min, max), orange.WithAlpha(0.10f * pulse));

        var width = max.X - min.X;
        var height = max.Y - min.Y;
        var bands = Math.Clamp((int) MathF.Round(height / 2.4f), 2, 6);
        var steps = Math.Clamp((int) MathF.Ceiling(width / 0.5f), 1, MaxBandSegments);

        for (var i = 0; i < bands; i++)
        {
            var phase = (time * 0.16f + i / (float) bands) % 1f;
            var y0 = min.Y + phase * height;
            var alpha = (0.16f + 0.14f * (0.5f + 0.5f * MathF.Sin(time * 1.6f + i * 1.7f))) * pulse;
            var color = orange.WithAlpha(alpha);

            var vertices = Vertices(steps * 6);
            var x = min.X;
            var step = width / steps;
            var last = new Vector2(x, y0 + 0.13f * MathF.Sin(time * 1.1f + i * 2.1f));
            var at = 0;

            for (var s = 0; s < steps; s++)
            {
                x += step;
                var point = new Vector2(x, y0 + 0.13f * MathF.Sin((x - min.X) * 1.2f + time * 1.1f + i * 2.1f));
                at = Quad(vertices, at, last, point, 0.045f);
                last = point;
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new ReadOnlySpan<Vector2>(vertices, 0, at), color);
        }

        // The border, as four quads in one call.
        var edge = Vertices(24);
        var edgeAt = 0;
        var bottomLeft = new Vector2(min.X, min.Y);
        var bottomRight = new Vector2(max.X, min.Y);
        var topRight = new Vector2(max.X, max.Y);
        var topLeft = new Vector2(min.X, max.Y);
        const float border = 0.06f;
        edgeAt = Quad(edge, edgeAt, bottomLeft, bottomRight, border);
        edgeAt = Quad(edge, edgeAt, bottomRight, topRight, border);
        edgeAt = Quad(edge, edgeAt, topRight, topLeft, border);
        edgeAt = Quad(edge, edgeAt, topLeft, bottomLeft, border);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
            new ReadOnlySpan<Vector2>(edge, 0, edgeAt), orange.WithAlpha(0.35f * pulse));
    }

    /// <summary>A world-space line with a little width, appended as two triangles; returns the new vertex count.</summary>
    private static int Quad(Vector2[] vertices, int at, Vector2 a, Vector2 b, float width)
    {
        var d = b - a;
        var length = d.Length();
        if (length < 0.0001f)
            return at;

        var n = new Vector2(-d.Y, d.X) / length * (width / 2);
        vertices[at] = a + n;
        vertices[at + 1] = a - n;
        vertices[at + 2] = b + n;
        vertices[at + 3] = a - n;
        vertices[at + 4] = b - n;
        vertices[at + 5] = b + n;
        return at + 6;
    }

    /// <summary>The shared vertex buffer, grown when a larger zone needs more room.</summary>
    private Vector2[] Vertices(int count)
    {
        if (_vertices.Length >= count)
            return _vertices;

        _vertices = new Vector2[Math.Max(count, _vertices.Length * 2)];
        return _vertices;
    }
}
