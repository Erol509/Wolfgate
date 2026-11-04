using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emp;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Stacks;
using Content.Shared.Wires;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Systems;

public sealed partial class ModCoreSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModCoreComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<ModCoreComponent, EmpAttemptEvent>(OnEmpAttempt);
        SubscribeLocalEvent<ModCoreRefuelableComponent, InteractUsingEvent>(OnRefuel);
        SubscribeLocalEvent<ModCoreComponent, InteractUsingEvent>(OnInsertCell);
        SubscribeLocalEvent<ModCoreComponent, InteractHandEvent>(OnRemoveCell);
    }

    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPowerCellSystem _powerCell = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedWiresSystem _wires = default!;

    private bool TryGetSuitCore(EntityUid core, out EntityUid suit)
    {
        suit = default;
        return _container.TryGetContainingContainer((core, null, null), out var container)
               && HasComp<ModSuitComponent>(container.Owner)
               && (suit = container.Owner).Valid;
    }

    private bool PanelOpen(EntityUid suit)
    {
        return TryComp<WiresPanelComponent>(suit, out var panel) && _wires.IsPanelOpen((suit, panel));
    }

    private void OnInsertCell(Entity<ModCoreComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled
            || !TryComp<PowerCellSlotComponent>(ent, out var cellSlot)
            || !TryGetSuitCore(ent, out var suit)
            || !PanelOpen(suit)
            || !_itemSlots.TryGetSlot(ent, cellSlot.CellSlotId, out var slot)
            || slot.Item != null)
            return;

        if (!HasComp<BatteryComponent>(args.Used) && !HasComp<PowerCellComponent>(args.Used))
            return;

        args.Handled = _itemSlots.TryInsert(ent, slot, args.Used, args.User);
    }

    private void OnRemoveCell(Entity<ModCoreComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled
            || !TryComp<PowerCellSlotComponent>(ent, out var cellSlot)
            || !TryGetSuitCore(ent, out var suit)
            || !PanelOpen(suit)
            || !_itemSlots.TryGetSlot(ent, cellSlot.CellSlotId, out var slot)
            || slot.Item == null)
            return;

        args.Handled = _itemSlots.TryEject(ent, slot, args.User, out _);
    }

    private bool TryGetBattery(EntityUid core, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Entity<BatteryComponent>? battery)
    {
        battery = null;

        if (TryComp<BatteryComponent>(core, out var direct))
        {
            battery = (core, direct);
            return true;
        }

        if (!TryComp<PowerCellSlotComponent>(core, out var slot)
            || !_itemSlots.TryGetSlot(core, slot.CellSlotId, out var itemSlot)
            || itemSlot.Item is not { } cell
            || !TryComp<BatteryComponent>(cell, out var cellBattery))
            return false;

        battery = (cell, cellBattery);
        return true;
    }

    private float GetCharge(Entity<BatteryComponent> battery)
    {
        var ev = new GetChargeEvent();
        RaiseLocalEvent(battery.Owner, ref ev);
        return ev.CurrentCharge;
    }
    private void OnPowerCellChanged(Entity<ModCoreComponent> ent, ref PowerCellChangedEvent args)
    {
        RelayToSuit(ent, ref args);
    }
    private void OnEmpAttempt(Entity<ModCoreComponent> ent, ref EmpAttemptEvent args)
    {
        RelayToSuit(ent, ref args);
    }

    private void RelayToSuit<T>(Entity<ModCoreComponent> ent, ref T args) where T : notnull
    {
        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container)
            && HasComp<ModSuitComponent>(container.Owner))
            RaiseLocalEvent(container.Owner, ref args);
    }
    private void OnRefuel(Entity<ModCoreRefuelableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_whitelist.IsValid(ent.Comp.Fuel, args.Used))
            return;

        args.Handled = true;

        if (!TryGetBattery(ent.Owner, out var battery))
            return;

        var missing = battery.Value.Comp.MaxCharge - GetCharge(battery.Value);
        if (missing <= 0f)
        {
            _popup.PopupClient(Loc.GetString("modsuit-core-full"), args.Target, args.User);
            return;
        }

        var wanted = Math.Max(1, (int) MathF.Ceiling(missing / ent.Comp.ChargePerFuel));
        var used = 1;
        if (TryComp<StackComponent>(args.Used, out var stack))
        {
            used = Math.Min(wanted, stack.Count);
            _stack.SetCount(args.Used, stack.Count - used, stack);
        }
        else
        {
            PredictedQueueDel(args.Used);
        }

        _battery.ChangeCharge(battery.Value.AsNullable(), used * ent.Comp.ChargePerFuel);
        _audio.PlayPredicted(ent.Comp.RefuelSound, args.Target, args.User);
        _popup.PopupClient(Loc.GetString("modsuit-core-refueled"), args.Target, args.User);
    }
}
