using Content.Server._WF.NpcCrew;
using Content.Shared.Projectiles;

namespace Content.Server._Mono.SpaceArtillery;

/// <summary>Reports damaging ship-weapon impacts through the existing projectile subscription.</summary>
public sealed partial class SpaceArtillerySystem
{
    private void ReportCrewHullHit(EntityUid uid, ProjectileHitEvent hit)
    {
        if (!hit.Damage.AnyPositive() || !TryComp<ProjectileComponent>(uid, out var projectile)
            || !TryComp<TransformComponent>(hit.Target, out var target) || !target.Anchored
            || target.GridUid is not { } grid)
            return;

        var source = projectile.Weapon ?? projectile.Shooter;
        if (source is not { } origin || !TryComp<TransformComponent>(origin, out var sourceTransform)
            || sourceTransform.GridUid is not { } attacker || attacker == grid)
            return;

        var ev = new WFCrewHullHitEvent(grid, attacker);
        RaiseLocalEvent(grid, ref ev, true);
    }
}
