using Content.Server.Power.EntitySystems;
using Content.Shared._WF.ItemSwitch;
using Content.Shared._WF.ItemSwitch.Components;
using Content.Shared.Examine;
using Content.Shared.Power.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._WF.ItemSwitch;

/// <summary>Server side of item switching: battery-powered states and examination.</summary>
public sealed class ItemSwitchSystem : SharedItemSwitchSystem
{
    [Dependency] private readonly SharedItemSwitchSystem _itemSwitch = default!;
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemSwitchComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ItemSwitchComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<ItemSwitchComponent, MeleeHitEvent>(OnMeleeAttack);
    }

    private void OnExamined(Entity<ItemSwitchComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.NeedsPower
            || !TryComp<BatteryComponent>(ent, out var battery)
            || !ent.Comp.States.TryGetValue(ent.Comp.State, out var state))
            return;

        var onMsg = ent.Comp.State != ent.Comp.DefaultState
            ? Loc.GetString("comp-stunbaton-examined-on")
            : Loc.GetString("comp-stunbaton-examined-off");
        args.PushMarkup(onMsg);

        if (ent.Comp.State == ent.Comp.DefaultState || state.EnergyPerUse <= 0)
            return;

        var count = (int) (battery.CurrentCharge / state.EnergyPerUse);
        args.PushMarkup(Loc.GetString("melee-battery-examine", ("color", "yellow"), ("count", count)));
    }

    private void CheckPowerAndSwitchState(EntityUid uid, ItemSwitchComponent component)
    {
        if (!component.NeedsPower
            || !TryComp<BatteryComponent>(uid, out var battery)
            || !component.States.TryGetValue(component.State, out var state))
            return;

        component.IsPowered = battery.CurrentCharge >= state.EnergyPerUse;

        if (component is { IsPowered: false, DefaultState: { } defaultState } && component.State != defaultState)
            _itemSwitch.Switch((uid, component), defaultState);
    }

    private void OnMeleeAttack(Entity<ItemSwitchComponent> ent, ref MeleeHitEvent args)
    {
        // Examining a melee weapon raises this event with IsHit false; do not spend power on it.
        if (!args.IsHit
            || !ent.Comp.NeedsPower
            || !ent.Comp.States.TryGetValue(ent.Comp.State, out var state)
            || state.EnergyPerUse <= 0)
            return;

        _battery.TryUseCharge(ent.Owner, state.EnergyPerUse);
    }

    private void OnAttemptMelee(EntityUid uid, ItemSwitchComponent component, ref AttemptMeleeEvent args)
    {
        CheckPowerAndSwitchState(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ItemSwitchComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            CheckPowerAndSwitchState(uid, comp);
        }
    }
}
