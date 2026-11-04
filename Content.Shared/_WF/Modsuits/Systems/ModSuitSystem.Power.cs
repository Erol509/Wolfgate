using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Popups;
using Content.Shared.Rounding;
using Robust.Shared.Network;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Systems;

public sealed partial class ModSuitSystem
{
    private static readonly TimeSpan PowerAlertInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextPowerAlertUpdate;

    public EntityUid? GetCore(Entity<ModSuitComponent> ent)
    {
        return _itemSlots.GetItemOrNull(ent, ent.Comp.CoreSlotId);
    }

    public Entity<BatteryComponent>? GetBattery(Entity<ModSuitComponent> ent)
    {
        if (GetCore(ent) is not { } core)
            return null;

        if (TryComp<BatteryComponent>(core, out var coreBattery))
            return (core, coreBattery);

        if (!TryComp<PowerCellSlotComponent>(core, out var cellSlot)
            || !_itemSlots.TryGetSlot(core, cellSlot.CellSlotId, out var slot)
            || slot.Item is not { } cell
            || !TryComp<BatteryComponent>(cell, out var cellBattery))
            return null;

        return (cell, cellBattery);
    }

    /// <summary>Reads a battery's charge through the shared power event, which the server implements.</summary>
    public float GetCharge(Entity<BatteryComponent> battery)
    {
        var ev = new GetChargeEvent();
        RaiseLocalEvent(battery.Owner, ref ev);
        return ev.CurrentCharge;
    }

    public bool HasCharge(Entity<ModSuitComponent> ent, float amount)
    {
        return GetBattery(ent) is { } battery && GetCharge(battery) >= amount;
    }

    public bool TryUseCharge(Entity<ModSuitComponent> ent, float amount)
    {
        if (amount <= 0f)
            return true;

        return GetBattery(ent) is { } battery && _battery.TryUseCharge(battery.AsNullable(), amount);
    }

    public float GetThemeDrain(Entity<ModSuitComponent> ent)
    {
        return _proto.Index(ent.Comp.Theme).ChargeDrain;
    }

    public float GetTotalDrain(Entity<ModSuitComponent> ent)
    {
        if (ent.Comp.State == ModSuitState.Inactive)
            return 0f;

        var drain = GetThemeDrain(ent);
        if (ent.Comp.State != ModSuitState.Active)
            return drain;

        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities)
        {
            if (!TryComp<ModModuleComponent>(module, out var moduleComp))
                continue;

            drain += moduleComp.IdlePower;
            if (moduleComp.Active)
                drain += moduleComp.ActivePower;
        }

        return drain;
    }

    /// <summary>Power drain is applied by <see cref="UpdatePowerDrain"/>; kept for call sites.</summary>
    public void RefreshDrain(Entity<ModSuitComponent> ent)
    {
    }
    private void OnPowerCellChanged(Entity<ModSuitComponent> ent, ref PowerCellChangedEvent args)
    {
        if (_timing.ApplyingState || ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);
        SetPowered(ent, !args.Ejected && HasCharge(ent, 0.01f));
        UpdatePowerAlert(ent);
    }
    private void OnSuitEntInserted(Entity<ModSuitComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);

        if (args.Container.ID != ent.Comp.CoreSlotId || _timing.ApplyingState)
            return;

        RefreshDrain(ent);
        SetPowered(ent, HasCharge(ent, 0.01f));
    }
    private void OnSuitEntRemoved(Entity<ModSuitComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);

        if (args.Container.ID != ent.Comp.CoreSlotId || _timing.ApplyingState)
            return;

        SetPowered(ent, false);
    }

    private void RaiseUpdated(EntityUid suit)
    {
        var ev = new ModSuitUpdatedEvent();
        RaiseLocalEvent(suit, ref ev);
    }

    private void SetPowered(Entity<ModSuitComponent> ent, bool powered)
    {
        if (ent.Comp.Powered == powered)
            return;

        ent.Comp.Powered = powered;
        Dirty(ent);

        if (ent.Comp.State == ModSuitState.Active)
        {
            if (powered)
            {
                ActivatePassiveModules(ent);
            }
            else
            {
                DeactivateAllModules(ent, true);
                if (ent.Comp.Wearer is { } wearer)
                    _popup.PopupClient(Loc.GetString("modsuit-out-of-power"), wearer, wearer, PopupType.MediumCaution);
            }
        }

        RefreshDrain(ent);
        RefreshSpeed(ent);
        UpdatePowerAlert(ent);
    }

    private void UpdatePowerAlerts()
    {
        if (_timing.CurTime < _nextPowerAlertUpdate)
            return;

        _nextPowerAlertUpdate = _timing.CurTime + PowerAlertInterval;

        var query = EntityQueryEnumerator<ModSuitComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Wearer != null && comp.State != ModSuitState.Inactive && ShouldStep(comp))
            {
                UpdatePowerDrain((uid, comp));
                UpdatePowerAlert((uid, comp));
            }
        }
    }

    /// <summary>Spends the suit's per-second drain on the server and cuts power when the core empties.</summary>
    private void UpdatePowerDrain(Entity<ModSuitComponent> ent)
    {
        if (_net.IsClient || GetBattery(ent) is not { } battery)
            return;

        var drain = GetTotalDrain(ent);
        if (drain <= 0f || GetCharge(battery) <= 0f)
            return;

        if (!_battery.TryUseCharge(battery.AsNullable(), drain))
            SetPowered(ent, false);
    }

    private void UpdatePowerAlert(Entity<ModSuitComponent> ent)
    {
        if (_timing.ApplyingState || ent.Comp.Wearer is not { } wearer)
            return;

        if (ent.Comp.State == ModSuitState.Inactive || GetBattery(ent) is not { } battery)
        {
            _alerts.ClearAlert(wearer, ent.Comp.PowerAlert);
            return;
        }

        var charge = GetCharge(battery);
        var severity = ContentHelpers.RoundToLevels(MathF.Max(0f, charge), battery.Comp.MaxCharge, 6);
        _alerts.ShowAlert(wearer, ent.Comp.PowerAlert, (short) severity);
    }
}
