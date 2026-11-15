using System.Numerics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.Systems;

/// <summary>Floating mobs just off a grid move relative to that grid, so stepping off a moving ship keeps its momentum.</summary>
public abstract partial class SharedMoverController
{
    /// <summary>How far past a mob's bounds a grid still counts as the one it floats beside.</summary>
    private const float WfGridFrameRange = 4f;

    private List<Entity<MapGridComponent>> _wfFrameGrids = new();

    /// <summary>
    /// The map velocity of the nearest grid at the mob's position, or zero when it is on a grid or no grid is in range.
    /// Friction and push-off apply relative to it, so a mob that leaves a moving ship drifts along with it.
    /// </summary>
    private Vector2 WfGridFrame(EntityUid uid, TransformComponent xform)
    {
        if (xform.GridUid != null || xform.MapUid == null)
            return Vector2.Zero;

        var position = _transform.GetWorldPosition(xform);
        var box = _lookup.GetWorldAABB(uid, xform).Enlarged(WfGridFrameRange);
        _wfFrameGrids.Clear();
        _mapSystem.FindGridsIntersecting(xform.MapID, box, ref _wfFrameGrids, approx: true);

        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var grid in _wfFrameGrids)
        {
            if (!PhysicsQuery.TryComp(grid, out var body) || body.BodyType == Robust.Shared.Physics.BodyType.Static)
                continue;

            var distance = (_transform.GetWorldPosition(grid.Owner) - position).LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = grid.Owner;
        }

        if (nearest is not { } frame || !PhysicsQuery.TryComp(frame, out var frameBody))
            return Vector2.Zero;

        var local = Vector2.Transform(position, _transform.GetInvWorldMatrix(frame));
        return PhysicsSystem.GetLinearVelocity(frame, local, frameBody);
    }
}
