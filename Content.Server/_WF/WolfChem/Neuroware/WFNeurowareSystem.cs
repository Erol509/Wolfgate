using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WF.WolfChem.Narcotics;
using Content.Server._WF.Synthetics;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server.Chat.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stunnable;
using Content.Server.Temperature.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared._WF.WolfChem.Neuroware;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfChem.Neuroware;

/// <summary>
/// Neuroware: data sticks load drug programs into a host brain, programs feed the shared intoxication levels at the
/// stick's integrity, damaged builds glitch, and the synthetic HUD shows what is running.
/// </summary>
public sealed class WFNeurowareSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private WFNarcoticGoodsSystem _goods = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private SynthBatterySystem _synthBattery = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private WFIntoxicationSystem _intox = default!;

    /// <summary>Integrity kept by a copy.</summary>
    public const float CopyIntegrity = 0.85f;

    /// <summary>Glitch chance a second at zero integrity.</summary>
    public const float GlitchChance = 0.15f;

    /// <summary>Share of battery capacity a second an overdriving program burns on a synthetic.</summary>
    public const float OverdriveDrain = 0.002f;

    /// <summary>Heat in joules a second an overdriving program adds to a synthetic.</summary>
    public const float OverdriveHeat = 1500f;

    private const string AdviceLine = "wf-neuroware-advice";
    private const string CrashLine = "wf-neuroware-crash-fault";
    private const string HabitLine = "wf-neuroware-habit-loop";
    private const int FalseFaults = 3;
    private static readonly TimeSpan FalseFaultTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScrambleTime = TimeSpan.FromSeconds(10);
    private static readonly string[] GlitchEmotes = { "Beep", "Buzz" };

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFNeurowareStickComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFNeurowareStickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WFNeurowareStickComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFNeurowareStickComponent, WFNeurowareLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<WolfmedSyntheticHudComponent, WolfmedSyntheticHudCollectEvent>(OnCollectFaults);
        SubscribeLocalEvent<BodyComponent, WFChassisRebootedEvent>(OnRebooted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        Tick(1f);
    }

    private void OnExamined(Entity<WFNeurowareStickComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Profile is not { } profile || !_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
        {
            args.PushMarkup(Loc.GetString("wf-neuroware-stick-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("wf-neuroware-stick-examine",
            ("program", Loc.GetString(proto.Neuroware.Program)),
            ("integrity", (int) MathF.Round(ent.Comp.Integrity * 100f)),
            ("loads", ent.Comp.Loads)));
    }

    private void OnAfterInteract(Entity<WFNeurowareStickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<WFNeurowareStickComponent>(target, out var blank))
        {
            args.Handled = true;
            TryCopy(ent, (target, blank), args.User);
            return;
        }

        if (!HasComp<BodyComponent>(target))
            return;

        args.Handled = true;
        TryStartLoad(ent, args.User, target);
    }

    private void OnUseInHand(Entity<WFNeurowareStickComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartLoad(ent, args.User, args.User);
    }

    /// <summary>Checks a stick can load into a target and starts the do-after.</summary>
    public bool TryStartLoad(Entity<WFNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target))
            return false;

        var self = user == target;
        var args = new DoAfterArgs(EntityManager, user, self ? stick.Comp.SelfDelay : stick.Comp.OtherDelay,
            new WFNeurowareLoadDoAfterEvent(), stick, target: target, used: stick)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        if (!self)
        {
            _popup.PopupEntity(Loc.GetString("wf-neuroware-load-target", ("user", Identity.Entity(user, EntityManager))),
                target, target, PopupType.MediumCaution);
        }

        return true;
    }

    private bool CanLoad(Entity<WFNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (stick.Comp.Profile == null)
        {
            _popup.PopupEntity(Loc.GetString("wf-neuroware-stick-blank"), stick, user);
            return false;
        }

        if (stick.Comp.Loads <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wf-neuroware-stick-spent"), stick, user);
            return false;
        }

        if (!_intox.IsNeurowareHost(target))
        {
            _popup.PopupEntity(Loc.GetString("wf-neuroware-no-port", ("target", Identity.Entity(target, EntityManager))),
                target, user);
            return false;
        }

        return true;
    }

    private void OnLoadDoAfter(Entity<WFNeurowareStickComponent> ent, ref WFNeurowareLoadDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = Load(ent, args.User, target);
    }

    /// <summary>Starts a stick's program on a host, using one load.</summary>
    public bool Load(Entity<WFNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target) || stick.Comp.Profile is not { } profile)
            return false;

        stick.Comp.Loads--;
        var running = EnsureComp<WFNeurowareComponent>(target);
        running.Programs.Add(new WFNeurowareProgram
        {
            Profile = profile,
            Remaining = stick.Comp.Amount,
            Rate = stick.Comp.Rate,
            Integrity = stick.Comp.Integrity,
        });

        if (_proto.TryIndex(profile, out var proto) && proto.Neuroware?.Banner is { } banner)
            _popup.PopupEntity(Loc.GetString(banner), target, target, PopupType.Medium);

        return true;
    }

    /// <summary>
    /// Copies a program onto a blank stick at <see cref="CopyIntegrity"/> of the source's integrity. The source keeps
    /// its loads, so the blanks and the loss per generation are what limit copies.
    /// </summary>
    public bool TryCopy(Entity<WFNeurowareStickComponent> source, Entity<WFNeurowareStickComponent> blank, EntityUid? user)
    {
        if (source.Comp.Profile == null || blank.Comp.Profile != null)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wf-neuroware-copy-fail"), blank, user.Value);
            return false;
        }

        if (source.Comp.Loads <= 0)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wf-neuroware-stick-spent"), source, user.Value);
            return false;
        }

        blank.Comp.Profile = source.Comp.Profile;
        blank.Comp.Integrity = source.Comp.Integrity * CopyIntegrity;
        blank.Comp.Amount = source.Comp.Amount;
        blank.Comp.Rate = source.Comp.Rate;
        blank.Comp.Loads = 1;
        _appearance.SetData(blank, WFNeurowareStickVisuals.Copied, true);
        _goods.SetNarcotic(blank, true);

        if (user != null)
            _popup.PopupEntity(Loc.GetString("wf-neuroware-copy-done"), blank, user.Value);
        return true;
    }

    /// <summary>Runs every program for <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<WFNeurowareComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            for (var i = comp.Programs.Count - 1; i >= 0; i--)
            {
                var program = comp.Programs[i];
                var delta = Math.Min(program.Remaining, program.Rate * seconds);
                program.Remaining -= delta;
                _intox.AddLevel(uid, program.Profile, delta * program.Integrity);

                if (_random.Prob(Math.Clamp((1f - program.Integrity) * GlitchChance * seconds, 0f, 1f)))
                    Glitch((uid, comp));

                if (program.Remaining <= 0f)
                    comp.Programs.RemoveAt(i);
            }

            if (comp.FalseFault != null && _timing.CurTime >= comp.FalseFaultUntil)
                comp.FalseFault = null;

            if (comp.Programs.Count == 0 && comp.FalseFault == null)
                RemCompDeferred<WFNeurowareComponent>(uid);
        }

        var hosts = EntityQueryEnumerator<WFIntoxicationComponent>();
        while (hosts.MoveNext(out var uid, out var intox))
        {
            if (Overdriving(intox) && _intox.IsNeurowareHost(uid))
                Overdrive(uid, seconds);
        }
    }

    private void Glitch(Entity<WFNeurowareComponent> ent)
    {
        switch (_random.Next(4))
        {
            case 0:
                _stun.TryStun(ent, TimeSpan.FromSeconds(1), true);
                break;
            case 1:
                _chat.TryEmoteWithChat(ent, _random.Pick(GlitchEmotes), ignoreActionBlocker: true, forceEmote: true);
                break;
            case 2:
                _status.TryAddStatusEffect(ent, "Stutter", ScrambleTime, true, "ScrambledAccent");
                break;
            default:
                ent.Comp.FalseFault = $"wf-neuroware-false-fault-{_random.Next(1, FalseFaults + 1)}";
                ent.Comp.FalseFaultUntil = _timing.CurTime + FalseFaultTime;
                break;
        }
    }

    /// <summary>True while a combat tier pushes the body past its rated speed.</summary>
    private bool Overdriving(WFIntoxicationComponent intox)
    {
        foreach (var (profile, level) in intox.Levels)
        {
            if (!_proto.TryIndex(profile, out var proto) || proto.Family != WFDrugFamily.Combat)
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0 && proto.Tiers[tier].Speed > 1f)
                return true;
        }

        return false;
    }

    /// <summary>A synthetic pays for Redline's speed in charge and heat instead of its heart.</summary>
    private void Overdrive(EntityUid body, float seconds)
    {
        if (TryComp<BatteryComponent>(body, out var own))
            _battery.UseCharge(body, own.MaxCharge * OverdriveDrain * seconds, own);
        else if (_powerCell.TryGetBatteryFromSlot(body, out var cell, out var cellBattery))
            _battery.UseCharge(cell.Value, cellBattery.MaxCharge * OverdriveDrain * seconds, cellBattery);
        else if (_synthBattery.TryGetBattery(body, out var synth))
            _battery.UseCharge(synth.Value, synth.Value.Comp.MaxCharge * OverdriveDrain * seconds, synth.Value.Comp);

        _temperature.ChangeHeat(body, OverdriveHeat * seconds, true);
    }

    private void OnCollectFaults(Entity<WolfmedSyntheticHudComponent> ent, ref WolfmedSyntheticHudCollectEvent args)
    {
        if (!_intox.IsNeurowareHost(ent))
            return;

        if (TryComp<WFIntoxicationComponent>(ent, out var intox))
        {
            foreach (var (profile, level) in intox.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
                    continue;

                var tier = proto.TierAt(level);
                if (tier < 0)
                    continue;

                var severity = tier == proto.Tiers.Count - 1 ? WolfmedSyntheticSeverity.Crit : WolfmedSyntheticSeverity.Warn;
                foreach (var fault in proto.Neuroware.Faults)
                {
                    Add(args.Faults, fault, severity);
                }
            }

            foreach (var _ in _intox.ActiveCrashes(intox))
            {
                Add(args.Faults, CrashLine, WolfmedSyntheticSeverity.Info);
                break;
            }
        }

        if (TryComp<WFDependenceComponent>(ent, out var dep))
        {
            foreach (var stage in dep.Stage.Values)
            {
                if (stage <= 0)
                    continue;

                Add(args.Faults, HabitLine, stage >= 2 ? WolfmedSyntheticSeverity.Warn : WolfmedSyntheticSeverity.Info);
                break;
            }
        }

        if (CompOrNull<WFNeurowareComponent>(ent)?.FalseFault is { } falseFault)
            Add(args.Faults, falseFault, WolfmedSyntheticSeverity.Crit);
    }

    private static void Add(List<WolfmedSyntheticFault> faults, string line, WolfmedSyntheticSeverity severity)
    {
        foreach (var existing in faults)
        {
            if (existing.Line == line)
                return;
        }

        faults.Add(new WolfmedSyntheticFault(TargetBodyPart.Head, line, severity, AdviceLine));
    }

    private void OnRebooted(Entity<BodyComponent> ent, ref WFChassisRebootedEvent args)
    {
        RemCompDeferred<WFNeurowareComponent>(ent);
        _intox.ClearLevels(ent);
    }
}
