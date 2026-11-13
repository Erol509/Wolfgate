using System.Numerics;
using Content.Shared._WF.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

// Optional zoom toward the cursor: the point under the mouse stays put while the range eases in or out.
public partial class ShuttleNavControl
{
    private RadarModeButton? _wfZoomToMouseButton;
    private bool _wfZoomToMouse;
    private bool _wfZoomHooked;
    private Vector2? _wfZoomWorld;
    private Vector2 _wfZoomPixel;
    private Vector2 _wfZoomSetOffset;

    /// <summary>Adds the zoom-to-cursor toggle to the radar's mode buttons.</summary>
    private void AddWfZoomToMouseButton(BoxContainer buttons)
    {
        _wfZoomToMouse = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarZoomToMouse);
        var button = _wfZoomToMouseButton = CreateRadarModeButton(RadarModeButtonIcon.WfZoomToMouse);
        button.ToolTip = Loc.GetString("wf-radar-zoom-to-mouse-tooltip");
        button.ToggleMode = true;
        button.Pressed = _wfZoomToMouse;
        button.OnToggled += args =>
        {
            _wfZoomToMouse = args.Pressed;
            _wfZoomWorld = null;
            _wfSettingsCfg.SetCVar(WolfgateCVars.RadarZoomToMouse, args.Pressed);
            _wfSettingsCfg.SaveToFile();
        };
        buttons.AddChild(button);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        if (_wfZoomToMouse && (Draggable || WfCockpitControls) && _coordinates != null && MinimapScale > 0f &&
            WfZoomChangesRange(args))
        {
            // The point under the cursor, turned from the view's old rotation into the one it has once detached.
            _wfZoomPixel = args.RelativePixelPosition;
            var before = InverseMapPosition(_wfZoomPixel);
            var oldRot = WfViewRotation();
            WfDetachForPan();
            _wfZoomWorld = (-WfViewRotation()).RotateVec(oldRot.RotateVec(before));
            _wfZoomSetOffset = Offset;
            if (!_wfZoomHooked)
            {
                _wfZoomHooked = true;
                WorldRangeChanged += WfFollowZoomAnchor;
            }
        }

        base.MouseWheel(args);
    }

    /// <summary>Whether this wheel step moves the zoom; at either limit it doesn't, and the view stays on the ship.</summary>
    private bool WfZoomChangesRange(GUIMouseWheelEventArgs args)
    {
        var direction = -args.Delta.Y / ScrollSensitivity;
        var target = Math.Clamp(ActualRadarRange * MathF.Pow(ZoomFactor, direction), WorldMinRange, WorldMaxRange);
        return !target.Equals(ActualRadarRange);
    }

    /// <summary>The rotation the view is drawn in: its anchor's when following it, otherwise north-up.</summary>
    private Angle WfViewRotation()
    {
        return _angleFollow && _coordinates is { } cord ? _transform.GetWorldRotation(cord.EntityId) : Angle.Zero;
    }

    /// <summary>Re-pans each eased zoom step so the anchored point stays under the cursor.</summary>
    private void WfFollowZoomAnchor(float range)
    {
        if (_wfZoomWorld is not { } world)
            return;
        // A drag, reset or recentre moved the view since the last step; let it win.
        if (Offset != _wfZoomSetOffset || _draggin || MinimapScale <= 0f)
        {
            _wfZoomWorld = null;
            return;
        }

        var local = (_wfZoomPixel - MidPointVector) / MinimapScale;
        Offset = TargetOffset = _wfZoomSetOffset = world - new Vector2(local.X, -local.Y);
        Recentering = false;
        _wasPanned = true;
        if (range.Equals(ActualRadarRange))
            _wfZoomWorld = null;
    }

    /// <summary>Pins a ship-anchored view to the map before panning, as a right-drag does.</summary>
    private void WfDetachForPan()
    {
        if (_relativePanning || _coordinates is not { } cord ||
            !EntManager.TryGetComponent<TransformComponent>(cord.EntityId, out var xform) ||
            xform.MapUid == cord.EntityId || EntManager.HasComponent<MapComponent>(cord.EntityId))
            return;

        PanToWorld();
        _coordinates = _transform.ToCoordinates(_transform.ToMapCoordinates(cord));
        PanToEnt();
        _wasPanned = true;
    }

    public sealed partial class RadarModeButton
    {
        /// <summary>Draws the zoom-to-cursor icon: a magnifier over an arrow tip.</summary>
        private void WfDrawZoomToMouse(DrawingHandleScreen handle, Vector2 center, float radius, Color ink)
        {
            var lens = center + new Vector2(-0.2f, -0.2f) * radius;
            handle.DrawCircle(lens, radius * 0.5f, ink, false);
            handle.DrawLine(lens + new Vector2(0.35f, 0.35f) * radius, center + new Vector2(0.8f, 0.8f) * radius, ink);
            handle.DrawLine(lens + new Vector2(-0.25f, 0f) * radius, lens + new Vector2(0.25f, 0f) * radius, ink);
            handle.DrawLine(lens + new Vector2(0f, -0.25f) * radius, lens + new Vector2(0f, 0.25f) * radius, ink);
        }
    }
}
