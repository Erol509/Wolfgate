using System.Numerics;
using Content.Client._WF.Encounters;
using Robust.Client.Graphics;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    /// <summary>The labels placed so far this frame, so ships of one encounter do not draw over each other.</summary>
    private readonly List<Box2> _encounterLabels = new();

    /// <summary>The ships of one side of one encounter, gathered for a frame.</summary>
    private readonly Dictionary<(NetEntity Encounter, string Side), (Vector2 Sum, int Count, bool Named, bool OnScope, Color Color)> _encounterGroups = new();

    /// <summary>
    /// Marks the visible encounters in the sector. A ship on the scope gets a diamond and its zones; the ships of one
    /// side share a single name and a single pair of zone captions. A side with no ship on the scope gets one bare
    /// arrow at the rim, however many ships it has, so far contacts don't crowd the scope.
    /// </summary>
    private void DrawEncounterMarkers(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var system = EntManager.System<WFEncounterMarkerClientSystem>();
        if (system.Markers.Count == 0)
            return;

        var centre = MidPointVector;
        var rim = MathF.Min(PixelSize.X, PixelSize.Y) / 2f - 20f * UIScale;
        var size = 6f * UIScale;
        _encounterLabels.Clear();
        _encounterGroups.Clear();
        foreach (var marker in system.Markers)
        {
            if (marker.Map != consoleXform.MapID)
                continue;

            var key = (marker.Encounter, marker.Side);
            var color = marker.Color ?? WFEncounterColors.Category(marker.Category);
            if (!_encounterGroups.TryGetValue(key, out var group))
                group = (Vector2.Zero, 0, false, false, color);

            var point = Vector2.Transform(system.GetPosition(marker), worldToView);
            var onScope = (point - centre).Length() <= rim;
            // The zones are each ship's own, but one caption does for the side.
            DrawEncounterZone(handle, point, marker.WarnRange, Color.FromHex("#ffd23f"), group.Named ? string.Empty : system.WarnLabel);
            DrawEncounterZone(handle, point, marker.AttackRange, Color.FromHex("#ff4b4b"), group.Named ? string.Empty : system.AttackLabel);
            if (onScope)
            {
                handle.DrawLine(point + new Vector2(0f, -size), point + new Vector2(size, 0f), color);
                handle.DrawLine(point + new Vector2(size, 0f), point + new Vector2(0f, size), color);
                handle.DrawLine(point + new Vector2(0f, size), point + new Vector2(-size, 0f), color);
                handle.DrawLine(point + new Vector2(-size, 0f), point + new Vector2(0f, -size), color);
                if (!group.Named)
                {
                    // Above the ship: its own IFF label hangs below and to the right, and the admin crew tag sits between.
                    var at = PlaceEncounterLabel(point + new Vector2(10f, -54f) * UIScale, handle.GetDimensions(Font, marker.Name, UIScale * 0.8f));
                    handle.DrawString(Font, at, marker.Name, UIScale * 0.8f, color);
                }
            }

            _encounterGroups[key] = (group.Sum + point, group.Count + 1, group.Named || onScope, group.OnScope || onScope, color);
        }

        foreach (var group in _encounterGroups.Values)
        {
            if (group.OnScope)
                continue;

            var offset = group.Sum / group.Count - centre;
            if (offset.LengthSquared() < 1f)
                continue;

            var direction = Vector2.Normalize(offset);
            var side = new Vector2(-direction.Y, direction.X);
            var tip = centre + direction * rim;
            var back = tip - direction * size * 2f;
            handle.DrawLine(tip, back + side * size, group.Color);
            handle.DrawLine(tip, back - side * size, group.Color);
            handle.DrawLine(back + side * size, back - side * size, group.Color);
        }
    }

    /// <summary>Moves a label down until it clears the ones already placed, and records where it ends up.</summary>
    private Vector2 PlaceEncounterLabel(Vector2 position, Vector2 size)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var box = Box2.FromDimensions(position, size);
            var clear = true;
            foreach (var placed in _encounterLabels)
            {
                if (!placed.Intersects(box))
                    continue;

                clear = false;
                break;
            }

            if (clear)
                break;

            position.Y += size.Y + 2f * UIScale;
        }

        _encounterLabels.Add(Box2.FromDimensions(position, size));
        return position;
    }

    /// <summary>A ring at a zone's radius around the ship, named at its top, when any of it is on the scope.</summary>
    private void DrawEncounterZone(DrawingHandleScreen handle, Vector2 centre, float range, Color color, string label)
    {
        if (range <= 0f)
            return;

        var radius = range * MinimapScale;
        if (centre.X + radius < 0f || centre.Y + radius < 0f || centre.X - radius > PixelSize.X || centre.Y - radius > PixelSize.Y)
            return;

        handle.DrawCircle(centre, radius, color.WithAlpha(0.8f), false);
        if (label.Length == 0)
            return;

        var dimensions = handle.GetDimensions(Font, label, UIScale * 0.7f);
        handle.DrawString(Font, centre + new Vector2(-dimensions.X / 2f, -radius - dimensions.Y - 2f * UIScale), label, UIScale * 0.7f, color);
    }
}
