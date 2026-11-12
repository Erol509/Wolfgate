using System.Linq;
using Content.Server.Body.Components;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Surgery;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._WF.Surgery;

/// <summary>Runs the operating computer: finds the adjacent table, tracks its patient and publishes state.</summary>
public sealed partial class OperatingComputerSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SurgicalOperationSystem _surgery = default!;
    [Dependency] private SurgicalSiteSystem _sites = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private float _updateAccumulator;
    private const float UpdateInterval = 0.5f;
    private readonly Dictionary<EntityUid, string> _signatures = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OperatingComputerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OperatingComputerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OperatingComputerComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SurgicalUnlockEvent>(OnUnlock);

        Subs.BuiEvents<OperatingComputerComponent>(OperatingComputerUiKey.Key, subs =>
        {
            subs.Event<OperatingComputerChangeZoneMessage>(OnChangeZone);
        });
    }

    private void OnMapInit(Entity<OperatingComputerComponent> ent, ref MapInitEvent args) => FindTable(ent);

    private void OnInteractUsing(Entity<OperatingComputerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<SurgeryToolComponent>(args.Used))
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnActivate(Entity<OperatingComputerComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnUnlock(SurgicalUnlockEvent args)
    {
        var query = EntityQueryEnumerator<OperatingComputerComponent>();
        while (query.MoveNext(out var uid, out var computer))
        {
            if (_ui.IsUiOpen(uid, OperatingComputerUiKey.Key))
                SendState((uid, computer), force: true);
        }
    }

    private void Open(Entity<OperatingComputerComponent> ent, EntityUid user)
    {
        FindTable(ent);
        UpdatePatient(ent);

        if (TryComp<TargetingComponent>(user, out var targeting))
            ent.Comp.Zone = targeting.Target;

        _ui.OpenUi(ent.Owner, OperatingComputerUiKey.Key, user);
        SendState(ent, force: true);
    }

    private void OnChangeZone(Entity<OperatingComputerComponent> ent, ref OperatingComputerChangeZoneMessage args)
    {
        var zone = (TargetBodyPart) args.Zone;
        if (!IsSingleZone(zone))
            return;

        ent.Comp.Zone = zone;
        Dirty(ent);

        if (TryComp<TargetingComponent>(args.Actor, out var targeting))
            targeting.Target = zone;

        SendState(ent, force: true);
    }

    private static bool IsSingleZone(TargetBodyPart zone) =>
        zone != 0 && (zone & (zone - 1)) == 0 && (zone & TargetBodyPart.All) != 0;

    private void FindTable(Entity<OperatingComputerComponent> ent)
    {
        ent.Comp.Table = null;
        var xform = Transform(ent.Owner);
        var query = EntityQueryEnumerator<OperatingTableComponent, TransformComponent>();

        while (query.MoveNext(out var table, out _, out var tableXform))
        {
            if (tableXform.MapID != xform.MapID)
                continue;

            var distance = (tableXform.WorldPosition - xform.WorldPosition).Length();
            if (distance > 1.6f)
                continue;

            ent.Comp.Table = table;
            break;
        }
    }

    private void UpdatePatient(Entity<OperatingComputerComponent> ent)
    {
        if (ent.Comp.Table is not { } table || TerminatingOrDeleted(table) ||
            !TryComp<StrapComponent>(table, out var strap))
        {
            ent.Comp.Patient = null;
            return;
        }

        ent.Comp.Patient = strap.BuckledEntities.Count > 0 ? strap.BuckledEntities.First() : null;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _updateAccumulator += frameTime;
        if (_updateAccumulator < UpdateInterval)
            return;

        _updateAccumulator = 0f;

        var query = EntityQueryEnumerator<OperatingComputerComponent>();
        while (query.MoveNext(out var uid, out var computer))
        {
            if (!_ui.IsUiOpen(uid, OperatingComputerUiKey.Key))
                continue;

            FindTable((uid, computer));
            UpdatePatient((uid, computer));
            SendState((uid, computer), force: false);
        }
    }

    private EntityUid? GetPart(EntityUid patient, TargetBodyPart zone)
    {
        if (!HasComp<BodyComponent>(patient))
            return null;

        var type = SharedSurgicalOperationSystem.ZoneToPartType(zone);
        var symmetry = SharedSurgicalOperationSystem.ZoneToSymmetry(zone);

        foreach (var (id, part) in _body.GetBodyChildren(patient))
        {
            if (part.PartType != type)
                continue;
            if (symmetry is { } wanted && part.Symmetry != wanted)
                continue;
            return id;
        }

        return null;
    }

    private void SendState(Entity<OperatingComputerComponent> ent, bool force)
    {
        var state = new OperatingComputerState
        {
            HasTable = ent.Comp.Table is { } table && !TerminatingOrDeleted(table),
            Zone = (int) ent.Comp.Zone,
        };

        var patient = ent.Comp.Patient;
        state.HasPatient = patient is { } && !TerminatingOrDeleted(patient.Value);

        EntityUid? part = null;
        if (state.HasPatient && patient is { } patientUid)
        {
            FillPatient(state, patientUid);
            part = GetPart(patientUid, (TargetBodyPart) state.Zone);
        }

        // The catalog is part of the same state: a second state type would be overwritten before it is sent.
        FillOperations(state, patient, part);

        var signature = BuildSignature(state);
        if (!force && _signatures.TryGetValue(ent.Owner, out var last) && last == signature)
            return;

        _signatures[ent.Owner] = signature;
        _ui.SetUiState(ent.Owner, OperatingComputerUiKey.Key, state);
    }

    private static string BuildSignature(OperatingComputerState state)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(state.HasTable).Append('|').Append(state.HasPatient).Append('|').Append(state.Zone);
        builder.Append('|').Append(state.PatientState).Append('|').Append((int) state.Health);
        builder.Append('|').Append((int) state.BloodLevel).Append('|').Append((int) state.Brute);
        builder.Append('|').Append((int) state.Burn).Append('|').Append((int) state.Toxin).Append('|').Append((int) state.Oxy);
        builder.Append('|').Append(string.Join(',', state.SurgeryState));

        foreach (var operation in state.Operations)
        {
            if (operation.ShowAsNext || operation.Locked)
                builder.Append('|').Append(operation.Id).Append(operation.ShowAsNext ? '+' : '-')
                    .Append(operation.Locked ? '+' : '-');
        }

        return builder.ToString();
    }

    private void FillPatient(OperatingComputerState state, EntityUid patient)
    {
        if (TryComp<DamageableComponent>(patient, out var damageable))
        {
            state.MaxHealth = (float) _thresholds.GetThresholdForState(patient, MobState.Dead);
            state.Health = MathF.Max(0f, state.MaxHealth - (float) damageable.TotalDamage);
            state.MinHealth = -state.MaxHealth / 2f;

            state.Brute = Group(damageable, "Brute");
            state.Burn = Group(damageable, "Burn");
            state.Toxin = Group(damageable, "Toxin");
            state.Oxy = Group(damageable, "Asphyxiation");
        }

        if (TryComp<BloodstreamComponent>(patient, out var blood) && blood.BloodSolution is { } solution)
        {
            state.BloodLevel = (float) solution.Comp.Solution.Volume;
            state.StandardBloodLevel = (float) solution.Comp.Solution.MaxVolume;

            if (_prototypes.TryIndex(blood.BloodReagent, out Content.Shared.Chemistry.Reagent.ReagentPrototype? reagent))
                state.BloodType = Loc.GetString(reagent.LocalizedName);
        }

        if (_mobState.IsDead(patient))
        {
            state.PatientState = Loc.GetString("operating-computer-patient-dead");
            state.PatientStateColor = "bad";
        }
        else if (_mobState.IsCritical(patient))
        {
            state.PatientState = Loc.GetString("operating-computer-patient-critical");
            state.PatientStateColor = "average";
        }
        else
        {
            state.PatientState = Loc.GetString("operating-computer-patient-stable");
            state.PatientStateColor = "good";
        }

        if (GetPart(patient, (TargetBodyPart) state.Zone) is { } part)
        {
            state.SurgeryState = _sites.GetReadableState(part);
            state.PatientMechanical = _sites.IsMechanical(part);
        }
        else
        {
            state.SurgeryState.Add(Loc.GetString("operating-computer-bodypart-missing"));
        }
    }

    private static float Group(DamageableComponent damageable, string group) =>
        damageable.DamagePerGroup.TryGetValue(group, out var value) ? (float) value : 0f;

    private void FillOperations(OperatingComputerState state, EntityUid? patient, EntityUid? part)
    {
        foreach (var operation in _surgery.AllOperations)
        {
            var entry = MakeOperationState(operation);
            entry.ShowAsNext = patient is { } patientUid && part is { } partUid &&
                               _surgery.IsStateAvailable(patientUid, partUid, operation,
                                   (TargetBodyPart) state.Zone);
            state.Operations.Add(entry);
        }
    }

    private OperatingComputerOperationState MakeOperationState(SurgicalOperationPrototype operation) => new()
    {
        Id = operation.ID,
        Name = Loc.GetString(operation.RndName is { } rnd ? rnd : operation.Name),
        Description = Loc.GetString(operation.RndDescription is { } rndDesc ? rndDesc : operation.Description),
        Tool = _surgery.GetRecommendedToolName(operation),
        Priority = operation.Flags.HasFlag(SurgicalOperationFlags.PriorityNextStep),
        Mechanic = operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical),
        Locked = operation.Flags.HasFlag(SurgicalOperationFlags.Locked) && !_surgery.IsUnlocked(operation.ID),
        Requirements = DescribeRequirements(operation),
    };

    private List<string> DescribeRequirements(SurgicalOperationPrototype operation)
    {
        var lines = new List<string>();

        var allRequired = new List<string>();
        foreach (var (flag, _) in SurgeryStateExtensions.Readable)
        {
            if (operation.RequiredStates.HasAny(flag))
                allRequired.Add(StateRequirement(flag, blocked: false));
        }

        if (!operation.Flags.HasFlag(SurgicalOperationFlags.StandingAllowed))
            allRequired.Add(Loc.GetString("surgery-req-lying"));
        if (!operation.Flags.HasFlag(SurgicalOperationFlags.IgnoreClothes))
            allRequired.Add(Loc.GetString("surgery-req-clothes"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical))
            allRequired.Add(Loc.GetString("surgery-req-mechanical"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Organic))
            allRequired.Add(Loc.GetString("surgery-req-organic"));

        if (allRequired.Count > 0)
        {
            lines.Add("!" + Loc.GetString("operating-computer-requirement-all"));
            lines.AddRange(allRequired);
        }

        if (operation.AnyRequiredStates != SurgeryState.None)
        {
            lines.Add("!" + Loc.GetString("operating-computer-requirement-any"));
            lines.AddRange(AnyRequirementStrings(operation.AnyRequiredStates));
        }

        if (operation.Flags.HasFlag(SurgicalOperationFlags.SelfOperable))
        {
            lines.Add("!" + Loc.GetString("operating-computer-requirement-optional"));
            lines.Add(Loc.GetString("surgery-req-self"));
        }

        var blocked = new List<string>();
        foreach (var (flag, _) in SurgeryStateExtensions.Readable)
        {
            if (operation.BlockedStates.HasAny(flag))
                blocked.Add(StateRequirement(flag, blocked: true));
        }

        if (operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical))
            blocked.Add(Loc.GetString("surgery-req-not-organic"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Organic))
            blocked.Add(Loc.GetString("surgery-req-not-mechanical"));

        if (blocked.Count > 0)
        {
            lines.Add("!" + Loc.GetString("operating-computer-requirement-blocked"));
            lines.AddRange(blocked);
        }

        return lines;
    }

    private List<string> AnyRequirementStrings(SurgeryState states)
    {
        var lines = new List<string>();

        if ((states & SurgeryStateExtensions.AllSkin) == SurgeryStateExtensions.AllSkin)
            lines.Add(Loc.GetString("surgery-req-skin-cut-or-open"));
        else if (states.HasAny(SurgeryState.SkinCut))
            lines.Add(Loc.GetString("surgery-req-skin-cut"));
        else if (states.HasAny(SurgeryState.SkinOpen))
            lines.Add(Loc.GetString("surgery-req-skin-open"));

        if ((states & SurgeryStateExtensions.AllBones) == SurgeryStateExtensions.AllBones)
            lines.Add(Loc.GetString("surgery-req-bone-sawed-or-drilled"));
        else if (states.HasAny(SurgeryState.BoneSawed))
            lines.Add(Loc.GetString("surgery-req-bone-sawed"));
        else if (states.HasAny(SurgeryState.BoneDrilled))
            lines.Add(Loc.GetString("surgery-req-bone-drilled"));

        if ((states & SurgeryStateExtensions.AllVessels) == SurgeryStateExtensions.AllVessels)
            lines.Add(Loc.GetString("surgery-req-vessels-either"));
        else if (states.HasAny(SurgeryState.VesselsUnclamped))
            lines.Add(Loc.GetString("surgery-req-vessels-unclamped"));
        else if (states.HasAny(SurgeryState.VesselsClamped))
            lines.Add(Loc.GetString("surgery-req-vessels-clamped"));

        if (states.HasAny(SurgeryState.OrgansCut))
            lines.Add(Loc.GetString("surgery-req-organs-cut"));
        if (states.HasAny(SurgeryState.CavityWidened))
            lines.Add(Loc.GetString("surgery-req-cavity-widened"));
        if (states.HasAny(SurgeryState.PlasticApplied))
            lines.Add(Loc.GetString("surgery-req-plastic-applied"));
        if (states.HasAny(SurgeryState.ProstheticUnsecured))
            lines.Add(Loc.GetString("surgery-req-prosthetic-unsecured"));

        return lines;
    }

    private string StateRequirement(SurgeryState state, bool blocked)
    {
        var suffix = blocked ? "-blocked" : string.Empty;
        return state switch
        {
            SurgeryState.SkinCut => Loc.GetString("surgery-req-skin-cut" + suffix),
            SurgeryState.SkinOpen => Loc.GetString("surgery-req-skin-open" + suffix),
            SurgeryState.VesselsUnclamped => Loc.GetString("surgery-req-vessels-unclamped" + suffix),
            SurgeryState.VesselsClamped => Loc.GetString("surgery-req-vessels-clamped" + suffix),
            SurgeryState.OrgansCut => Loc.GetString("surgery-req-organs-cut" + suffix),
            SurgeryState.BoneDrilled => Loc.GetString("surgery-req-bone-drilled" + suffix),
            SurgeryState.BoneSawed => Loc.GetString("surgery-req-bone-sawed" + suffix),
            SurgeryState.PlasticApplied => Loc.GetString("surgery-req-plastic-applied" + suffix),
            SurgeryState.ProstheticUnsecured => Loc.GetString("surgery-req-prosthetic-unsecured" + suffix),
            SurgeryState.CavityWidened => Loc.GetString("surgery-req-cavity-widened" + suffix),
            _ => string.Empty,
        };
    }
}
