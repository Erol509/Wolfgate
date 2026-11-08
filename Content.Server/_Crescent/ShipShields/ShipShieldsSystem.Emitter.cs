// WOLFGATE: restore the licence header removed by Kill Spdx
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server._Crescent.ShipShields.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Server.Power.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Components;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Station.Systems;
using Robust.Shared.Audio.Systems;
using Content.Shared.Examine;
using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Crescent.ShipShields;

public partial class ShipShieldsSystem
{
    private const float MAX_EMP_DAMAGE = 10000f;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    public void InitializeEmitters()
    {
        SubscribeLocalEvent<ShipShieldEmitterComponent, ShieldDeflectedEvent>(OnShieldDeflected);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipShieldEmitterComponent, ComponentRemove>(OnRemoved);
    }


    private void OnRemoved(Entity<ShipShieldEmitterComponent> owner, ref ComponentRemove remove)
    {
        var parent = Transform(owner.Owner).GridUid;
        if (parent is null)
            return;
        // WOLFGATE(ShipShields) START: removing a standby generator leaves the active field intact
        // UnshieldEntity(parent.Value, null);
        RemoveWolfgateEmitterShield(owner.Owner, owner.Comp);
        // WOLFGATE END
    }

    private void OnShieldDeflected(EntityUid uid, ShipShieldEmitterComponent component, ShieldDeflectedEvent args)
    {
        if (TryComp<EmpOnTriggerComponent>(args.Deflected, out var emp))
        {
            component.Damage += Math.Clamp(emp.EnergyConsumption, 0f, MAX_EMP_DAMAGE);
            // WOLFGATE(ShipShields) START: preserve the EMP while absorbing explosive and spawn payloads
            // _trigger.Trigger(args.Deflected);
            PulseWolfgateAbsorbedEmp(args.Deflected, emp);
            // WOLFGATE END
        }

        if (TryComp<ExplosiveComponent>(args.Deflected, out var exp) && _prototypeManager.TryIndex(exp.ExplosionType, out var type))
        {
            component.Damage += exp.TotalIntensity * (float)type.DamagePerIntensity.GetTotal();
        }

        component.Damage += (float)args.Projectile.Damage.GetTotal();
        args.Projectile.ProjectileSpent = true;

        QueueDel(args.Deflected);
    }

    private void OnExamined(EntityUid uid, ShipShieldEmitterComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        ExamineWolfgateGeneratorStats(uid, component, args); // WOLFGATE(ShipShields): show runtime specifications for generator comparison.
        args.PushMarkup(Loc.GetString("shield-emitter-examine", ("basedraw", component.BaseDraw), ("additional", CalculateLoadDamage(component))));
        if (HasComp<ShipShieldDisabledGridComponent>(Transform(uid).GridUid))
            args.PushMarkup(Loc.GetString("shield-emitter-examine-invalid-grid"));
    }

    private static float CalculateLoadDamage(ShipShieldEmitterComponent emitter)
    {
        return (float)Math.Clamp(Math.Pow(emitter.Damage, emitter.DamageExp) * emitter.PowerModifier, 0f, emitter.MaxDraw);
    }

    private void AdjustEmitterLoad(EntityUid uid, ShipShieldEmitterComponent? emitter = null, ApcPowerReceiverComponent? receiver = null)
    {
        if (!Resolve(uid, ref emitter, ref receiver))
            return;

        receiver.Load = emitter.BaseDraw + CalculateLoadDamage(emitter);
    }
}
