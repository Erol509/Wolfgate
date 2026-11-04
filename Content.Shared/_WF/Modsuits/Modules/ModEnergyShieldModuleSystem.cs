using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModEnergyShieldModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModEnergyShieldModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModEnergyShieldModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModEnergyShieldActiveComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<ModEnergyShieldModuleComponent, ExaminedEvent>(OnExamined);
    }

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ModEnergyShieldModuleComponent>();
        while (query.MoveNext(out var uid, out var shield))
        {
            if (shield.Charges >= shield.MaxCharges || now < shield.NextCharge)
                continue;

            shield.Charges++;
            shield.NextCharge = now + shield.ChargeIncrement;
            Dirty(uid, shield);
        }
    }
    private void OnActivated(Entity<ModEnergyShieldModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is not { } wearer)
            return;

        var active = EnsureComp<ModEnergyShieldActiveComponent>(wearer);
        active.Module = ent;
        Dirty(wearer, active);
    }
    private void OnDeactivated(Entity<ModEnergyShieldModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemComp<ModEnergyShieldActiveComponent>(wearer);
    }
    private void OnDamageModify(Entity<ModEnergyShieldActiveComponent> ent, ref DamageModifyEvent args)
    {
        if (!TryComp<ModEnergyShieldModuleComponent>(ent.Comp.Module, out var shield)
            || shield.Charges <= 0
            || args.Origin is not { } origin
            || origin == ent.Owner
            || args.Damage.GetTotal() <= 0
            || !_modSuit.TryGetModuleSuit(ent.Comp.Module, out var suit))
            return;

        args.Damage = new DamageSpecifier();
        shield.Charges--;
        shield.NextCharge = _timing.CurTime + shield.RechargeDelay;
        Dirty(ent.Comp.Module, shield);

        _modSuit.TryUseCharge(suit, shield.BlockPower);

        if (_net.IsClient)
            return;

        _audio.PlayPvs(shield.BlockSound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("modsuit-energy-shield-block"), ent.Owner, PopupType.Medium);
    }
    private void OnExamined(Entity<ModEnergyShieldModuleComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("modsuit-energy-shield-examine",
            ("charges", ent.Comp.Charges),
            ("max", ent.Comp.MaxCharges)));
    }
}

/// <summary>Marks a wearer protected by an active energy shield module.</summary>
[RegisterComponent]
public sealed partial class ModEnergyShieldActiveComponent : Component
{
    [DataField]
    public EntityUid Module;
}
