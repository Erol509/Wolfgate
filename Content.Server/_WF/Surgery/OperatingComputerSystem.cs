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
using Content.Shared.Power.EntitySystems;
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
    [Dependency] private SharedPowerReceiverSystem _power = default!;
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

        // A table also answers an empty hand; the dedicated console follows the surgical-tool convention.
        if (HasComp<SurgeryToolComponent>(args.Used) || HasComp<OperatingTableComponent>(ent.Owner))
        {
            Open(ent, args.User);
            args.Handled = true;
        }
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
                SendCatalog((uid, computer));
        }
    }

    private void Open(Entity<OperatingComputerComponent> ent, EntityUid user)
    {
        if (ent.Comp.RequiresPower && !_power.IsPowered(ent.Owner))
            return;

        FindTable(ent);
        UpdatePatient(ent);

        if (TryComp<TargetingComponent>(user, out var targeting))
            ent.Comp.Zone = targeting.Target;

        _ui.OpenUi(ent.Owner, OperatingComputerUiKey.Key, user);
        SendCatalog(ent);
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
        // An operating table hosts its own UI.
        if (HasComp<OperatingTableComponent>(ent.Owner))
        {
            ent.Comp.Table = ent.Owner;
            return;
        }

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

        if (state.HasPatient && patient is { } patientUid)
        {
            FillPatient(state, patientUid);
            FillAvailable(state, patientUid);
        }

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
        builder.Append('|').Append(string.Join(',', state.AvailableOperations));
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
            state.SurgeryState = _sites.GetReadableState(part);
        else
            state.SurgeryState.Add(Loc.GetString("operating-computer-bodypart-missing"));
    }

    private static float Group(DamageableComponent damageable, string group) =>
        damageable.DamagePerGroup.TryGetValue(group, out var value) ? (float) value : 0f;

    private void FillAvailable(OperatingComputerState state, EntityUid patient)
    {
        if (GetPart(patient, (TargetBodyPart) state.Zone) is not { } part)
            return;

        foreach (var operation in _surgery.AllOperations)
        {
            if (_surgery.IsStateAvailable(patient, part, operation))
                state.AvailableOperations.Add(operation.ID);
        }
    }

    private void SendCatalog(Entity<OperatingComputerComponent> ent)
    {
        var catalog = new OperatingComputerCatalogState();

        foreach (var operation in _surgery.AllOperations)
        {
            catalog.Operations.Add(new OperatingComputerOperationState
            {
                Id = operation.ID,
                Name = Loc.GetString(operation.RndName is { } rnd ? rnd : operation.Name),
                Description = Loc.GetString(operation.RndDescription is { } rndDesc ? rndDesc : operation.Description),
                Tool = _surgery.GetRecommendedToolName(operation),
                Priority = operation.Flags.HasFlag(SurgicalOperationFlags.PriorityNextStep),
                Mechanic = operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical),
                Locked = operation.Flags.HasFlag(SurgicalOperationFlags.Locked) && !_surgery.IsUnlocked(operation.ID),
                Requirements = DescribeRequirements(operation),
            });
        }

        _ui.SetUiState(ent.Owner, OperatingComputerUiKey.Key, catalog);
    }

    private List<string> DescribeRequirements(SurgicalOperationPrototype operation)
    {
        var lines = new List<string>();

        foreach (var (flag, locId) in SurgeryStateExtensions.Readable)
        {
            if (operation.RequiredStates.HasAny(flag))
                lines.Add(Loc.GetString("surgery-req-required", ("state", Loc.GetString(locId))));
        }

        if (operation.AnyRequiredStates != SurgeryState.None)
        {
            var any = SurgeryStateExtensions.Readable
                .Where(pair => operation.AnyRequiredStates.HasAny(pair.State))
                .Select(pair => Loc.GetString(pair.LocId));
            lines.Add(Loc.GetString("surgery-req-any", ("state", string.Join(", ", any))));
        }

        foreach (var (flag, locId) in SurgeryStateExtensions.Readable)
        {
            if (operation.BlockedStates.HasAny(flag))
                lines.Add(Loc.GetString("surgery-req-blocked", ("state", Loc.GetString(locId))));
        }

        if (!operation.Flags.HasFlag(SurgicalOperationFlags.StandingAllowed))
            lines.Add(Loc.GetString("surgery-req-lying"));
        if (!operation.Flags.HasFlag(SurgicalOperationFlags.IgnoreClothes))
            lines.Add(Loc.GetString("surgery-req-clothes"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical))
            lines.Add(Loc.GetString("surgery-req-mechanical"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Organic))
            lines.Add(Loc.GetString("surgery-req-organic"));
        if (operation.Flags.HasFlag(SurgicalOperationFlags.SelfOperable))
            lines.Add(Loc.GetString("surgery-req-self"));

        return lines;
    }
}
