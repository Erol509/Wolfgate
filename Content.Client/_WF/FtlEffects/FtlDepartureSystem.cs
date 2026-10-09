using Content.Shared._WF.FtlEffects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WF.FtlEffects;

/// <summary>Installs the hull-fitted hyperspace departure overlay.</summary>
public sealed partial class FtlDepartureSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        base.Initialize();
        var background = new FtlMotionBackgroundOverlay(this);
        _overlays.AddOverlay(background);
        _overlays.AddOverlay(new FtlMotionOverlay(this, background));
        _overlays.AddOverlay(new FtlDepartureOverlay(this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<FtlDepartureOverlay>();
        _overlays.RemoveOverlay<FtlMotionOverlay>();
        _overlays.RemoveOverlay<FtlMotionBackgroundOverlay>();
        base.Shutdown();
    }

    /// <summary>The crew's camera stays aboard; outside observers see the ship streak past.</summary>
    public float Motion(EntityUid grid, FtlDepartureComponent effect)
    {
        if (_player.LocalEntity is { } player && TryComp(player, out TransformComponent? xform) && xform.GridUid == grid)
            return 0f;
        return FtlDepartureTiming.Motion(_timing.CurTime, effect.Departure, effect.Entered, effect.Arriving);
    }

    /// <summary>Skips the background copy unless a rushing hull can touch this viewport.</summary>
    public bool HasMotion(MapId map, Box2 view)
    {
        var query = EntityQueryEnumerator<FtlDepartureComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var effect, out var grid, out var xform))
        {
            var motion = Motion(uid, effect);
            if (xform.MapID != map || MathF.Abs(motion) < 0.0001f)
                continue;
            var matrix = _transforms.GetWorldMatrix(xform);
            var moved = FtlConeGeometry.MotionTransform(grid.LocalAABB, motion) * matrix;
            if (view.Intersects(matrix.TransformBox(grid.LocalAABB.Enlarged(0.5f))) ||
                view.Intersects(moved.TransformBox(grid.LocalAABB.Enlarged(0.5f))))
                return true;
        }
        return false;
    }
}
