using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._WF.Surgery;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Research.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Implants;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;

namespace Content.Server._WF.Surgery;

/// <summary>Server half of the state-driven surgery engine.</summary>
public sealed partial class SurgicalOperationSystem : SharedSurgicalOperationSystem
{
    private const float FailureThreshold = 2.5f;
    private const float FailureChancePerSecond = 10f;

    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedSubdermalImplantSystem _implants = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfmedSurgeryConditionSystem _wolfmedConditions = default!;

    private readonly HashSet<string> _unlocked = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SurgeryTargetComponent, InteractUsingEvent>(OnBodyInteractUsing);
        SubscribeLocalEvent<BodyPartComponent, InteractUsingEvent>(OnPartInteractUsing);
        SubscribeLocalEvent<SurgeryTargetComponent, InteractHandEvent>(OnBodyInteractHand);

        Subs.BuiEvents<SurgeryTargetComponent>(SurgeryUIKey.Key, subs =>
        {
            subs.Event<SurgicalRadialChooseMessage>(OnRadialChosen);
            subs.Event<SurgicalOrganChooseMessage>(OnOrganChosen);
            subs.Event<SurgicalOrganCancelMessage>(OnOrganCancelled);
        });

        SubscribeLocalEvent<SurgeryTargetComponent, SurgicalOperationDoAfterEvent>(OnOperationDoAfter);
        SubscribeLocalEvent<BodyPartComponent, SurgicalOperationDoAfterEvent>(OnOperationDoAfterPart);
        SubscribeLocalEvent<SurgeryTargetComponent, SurgeryStepChosenBuiMsg>(OnLegacyStepChosenBody);
        SubscribeLocalEvent<BodyPartComponent, SurgeryStepChosenBuiMsg>(OnLegacyStepChosenPart);
        SubscribeLocalEvent<TechnologyDatabaseComponent, TechnologyDatabaseModifiedEvent>(OnTechnologyDatabaseModified);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    protected override bool IsOperationUnlocked(SurgicalOperationPrototype operation) =>
        !operation.Flags.HasFlag(SurgicalOperationFlags.Locked) || _unlocked.Contains(operation.ID);

    private void OnRoundRestart(RoundRestartCleanupEvent args) => _unlocked.Clear();

    private void OnTechnologyDatabaseModified(Entity<TechnologyDatabaseComponent> ent,
        ref TechnologyDatabaseModifiedEvent args) =>
        UnlockFromDatabase(ent.Comp);

    private void UnlockFromDatabase(TechnologyDatabaseComponent database)
    {
        foreach (var technology in database.UnlockedTechnologies)
        {
            if (!_prototypes.TryIndex<SurgicalResearchPrototype>(technology, out var research))
                continue;

            foreach (var operation in research.Operations)
            {
                if (_unlocked.Add(operation))
                    RaiseLocalEvent(new SurgicalUnlockEvent(operation));
            }
        }
    }

    /// <summary>Whether an operation is available to surgeons on this station.</summary>
    public bool IsUnlocked(string operationId) =>
        _prototypes.TryIndex(operationId, out SurgicalOperationPrototype? operation) &&
        (!operation.Flags.HasFlag(SurgicalOperationFlags.Locked) || _unlocked.Contains(operationId));

    /// <summary>Generic state steps, for legacy callers that still address a surgery step by prototype id.</summary>
    private static readonly Dictionary<string, string> GenericStepOperations = new()
    {
        ["SurgeryStepOpenIncisionScalpel"] = "WFInciseSkin",
        ["SurgeryStepCarefulIncisionScalpel"] = "WFInciseSkin",
        ["SurgeryStepRetractSkin"] = "WFRetractSkin",
        ["SurgeryStepClampBleeders"] = "WFClampBleeders",
        ["SurgeryStepSawBones"] = "WFSawBone",
        ["SurgeryStepClampInternalBleeders"] = "WFInciseOrgans",
        ["SurgeryStepCloseIncision"] = "WFMendSkin",
        ["SurgeryStepSealTendWound"] = "WFMendSkin",
        ["SurgeryStepSealWounds"] = "WFMendSkin",
    };

    private void OnLegacyStepChosenBody(Entity<SurgeryTargetComponent> ent, ref SurgeryStepChosenBuiMsg args) =>
        HandleLegacyStepChosen(ent.Owner, ref args);

    private void OnLegacyStepChosenPart(Entity<BodyPartComponent> ent, ref SurgeryStepChosenBuiMsg args) =>
        HandleLegacyStepChosen(ent.Owner, ref args);

    /// <summary>
    /// Adapter for legacy callers (the autodoc, integration tests) that still name a surgery step. It starts the
    /// equivalent state-driven operation and then runs the same server-authoritative do-after as the radial.
    /// </summary>
    private void HandleLegacyStepChosen(EntityUid eventTarget, ref SurgeryStepChosenBuiMsg args)
    {
        if (!_netManager.IsServer)
            return;

        var user = args.Actor;

        EntityUid body;
        if (GetEntity(args.Entity) is { Valid: true } entityBody)
            body = entityBody;
        else if (HasComp<SurgeryTargetComponent>(eventTarget))
            body = eventTarget;
        else
            return;

        var chosenPart = GetEntity(args.Part);
        if (!chosenPart.Valid || TerminatingOrDeleted(chosenPart))
            return;

        if (!TryComp<BodyPartComponent>(chosenPart, out var partComp))
            return;

        AimAtPart(user, partComp);

        var tool = _handsSystem.GetActiveItem(user);
        var operation = FindOperationForLegacyStep(args.Surgery.Id, args.Step.Id);
        if (operation == null)
            return;

        var option = GetAvailableOperations(user, body, tool)
            .FirstOrDefault(candidate => candidate.Operation == operation);

        if (option != null)
            StartOperation(user, body, tool, option, legacyTiming: true);
    }

    private SurgicalOperationPrototype? FindOperationForLegacyStep(string surgery, string step)
    {
        if (GenericStepOperations.TryGetValue(step, out var genericId) &&
            _prototypes.TryIndex(genericId, out SurgicalOperationPrototype? generic))
        {
            return generic;
        }

        foreach (var operation in AllOperations)
        {
            if (operation.Surgery.Id == surgery && operation.Step?.Id == step)
                return operation;
        }

        foreach (var operation in AllOperations)
        {
            if (operation.Step?.Id == step)
                return operation;
        }

        return null;
    }

    private void AimAtPart(EntityUid user, BodyPartComponent part)
    {
        if (!TryComp<TargetingComponent>(user, out var targeting) || part.PartType == BodyPartType.Other)
            return;

        var zone = part.PartType switch
        {
            BodyPartType.Head => TargetBodyPart.Head,
            BodyPartType.Torso => TargetBodyPart.Torso,
            BodyPartType.Arm => part.Symmetry == BodyPartSymmetry.Left ? TargetBodyPart.LeftArm : TargetBodyPart.RightArm,
            BodyPartType.Hand => part.Symmetry == BodyPartSymmetry.Left ? TargetBodyPart.LeftHand : TargetBodyPart.RightHand,
            BodyPartType.Leg => part.Symmetry == BodyPartSymmetry.Left ? TargetBodyPart.LeftLeg : TargetBodyPart.RightLeg,
            BodyPartType.Foot => part.Symmetry == BodyPartSymmetry.Left ? TargetBodyPart.LeftFoot : TargetBodyPart.RightFoot,
            _ => TargetBodyPart.Torso,
        };

        targeting.Target = zone;
    }

    private void OnBodyInteractUsing(Entity<SurgeryTargetComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !args.Used.IsValid())
            return;

        if (TryBeginFromInteraction(args.User, ent.Owner, args.Used))
            args.Handled = true;
    }

    private void OnPartInteractUsing(Entity<BodyPartComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !args.Used.IsValid())
            return;

        if (TryBeginFromInteraction(args.User, ent.Owner, args.Used))
            args.Handled = true;
    }

    private void OnBodyInteractHand(Entity<SurgeryTargetComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (TryBeginFromInteraction(args.User, ent.Owner, null))
            args.Handled = true;
    }

    private bool TryBeginFromInteraction(EntityUid user, EntityUid target, EntityUid? tool)
    {
        if (!_netManager.IsServer)
            return false;

        var options = GetAvailableOperations(user, target, tool, collapseOrganOptions: true);
        if (options.Count == 0)
            return false;

        // Sort by priority then name so the radial is stable.
        options.Sort((a, b) =>
        {
            var priority = a.Operation.Priority.CompareTo(b.Operation.Priority);
            return priority != 0 ? priority : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        if (options.Count == 1 && IsOrganPickerOption(options[0]))
        {
            if (!_ui.TryOpenUi(target, SurgeryUIKey.Key, user))
                return StartFirstExpanded(user, target, tool);

            OpenOrganPicker(target, user, options[0].Part);
            return true;
        }

        if (options.Count == 1)
        {
            StartOperation(user, target, tool, options[0]);
            return true;
        }

        var radial = new List<SurgicalRadialOption>(options.Count);
        for (var i = 0; i < options.Count; i++)
            radial.Add(new SurgicalRadialOption(i, options[i]));

        // A detached limb (or a mob without a registered interface) has no wheel to anchor; run the first
        // choice instead of swallowing the interaction.
        if (!_ui.TryOpenUi(target, SurgeryUIKey.Key, user))
        {
            StartOperation(user, target, tool, options[0]);
            return true;
        }

        _ui.SetUiState(target, SurgeryUIKey.Key,
            new SurgicalRadialState(GetNetEntity(target), GetNetEntity(options[0].Part), radial));
        return true;
    }

    /// <summary>The radial shows one organ slice; the actual organs are chosen in a second window.</summary>
    private static bool IsOrganPickerOption(SurgicalOperationOption option) =>
        option.Operation.Kind == SurgicalOperationKind.OrganManipulation &&
        option.Organ == null && !option.Inserting;

    private bool StartFirstExpanded(EntityUid user, EntityUid target, EntityUid? tool)
    {
        var expanded = GetAvailableOperations(user, target, tool);
        if (expanded.Count == 0)
            return false;

        StartOperation(user, target, tool, expanded[0]);
        return true;
    }

    /// <summary>Publishes the organ list the surgeon picks from after choosing the organ-manipulation slice.</summary>
    private void OpenOrganPicker(EntityUid target, EntityUid user, EntityUid part)
    {
        var tool = _handsSystem.GetActiveItem(user);
        var options = GetAvailableOperations(user, target, tool)
            .Where(option => option.Operation.Kind == SurgicalOperationKind.OrganManipulation &&
                             option.Part == part)
            .ToList();

        if (options.Count == 0)
        {
            _ui.CloseUi(target, SurgeryUIKey.Key, user);
            return;
        }

        var choices = new List<SurgicalOrganOption>(options.Count);
        foreach (var option in options)
        {
            choices.Add(new SurgicalOrganOption
            {
                Organ = option.Organ is { } organ ? GetNetEntity(organ) : null,
                Inserting = option.Inserting,
                Name = option.Name,
            });
        }

        _ui.SetUiState(target, SurgeryUIKey.Key,
            new SurgicalOrganSelectState(GetNetEntity(target), choices));
    }

    private void OnRadialChosen(Entity<SurgeryTargetComponent> ent, ref SurgicalRadialChooseMessage args)
    {
        var user = args.Actor;
        var tool = _handsSystem.GetActiveItem(user);

        var options = GetAvailableOperations(user, ent.Owner, tool, collapseOrganOptions: true);
        if (args.Index < 0 || args.Index >= options.Count)
            return;

        options.Sort((a, b) =>
        {
            var priority = a.Operation.Priority.CompareTo(b.Operation.Priority);
            return priority != 0 ? priority : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        var chosen = options[args.Index];
        if (IsOrganPickerOption(chosen))
        {
            OpenOrganPicker(ent.Owner, user, chosen.Part);
            return;
        }

        StartOperation(user, ent.Owner, tool, chosen);
        _ui.CloseUi(ent.Owner, SurgeryUIKey.Key, user);
    }

    private void OnOrganChosen(Entity<SurgeryTargetComponent> ent, ref SurgicalOrganChooseMessage args)
    {
        var user = args.Actor;
        var tool = _handsSystem.GetActiveItem(user);
        var organ = args.Organ is { } netOrgan ? GetEntity(netOrgan) : (EntityUid?) null;

        foreach (var option in GetAvailableOperations(user, ent.Owner, tool))
        {
            if (option.Operation.Kind != SurgicalOperationKind.OrganManipulation ||
                option.Inserting != args.Inserting)
                continue;

            if (organ is { } wanted)
            {
                if (option.Organ != wanted)
                    continue;
            }
            else if (option.Organ != null)
            {
                continue;
            }

            StartOperation(user, ent.Owner, tool, option);
            _ui.CloseUi(ent.Owner, SurgeryUIKey.Key, user);
            return;
        }

        _ui.CloseUi(ent.Owner, SurgeryUIKey.Key, user);
    }

    private void OnOrganCancelled(Entity<SurgeryTargetComponent> ent, ref SurgicalOrganCancelMessage args) =>
        _ui.CloseUi(ent.Owner, SurgeryUIKey.Key, args.Actor);

    /// <summary>Starts the do-after for one option.</summary>
    public void StartOperation(EntityUid user, EntityUid target, EntityUid? tool, SurgicalOperationOption option, bool legacyTiming = false)
    {
        if (_net.IsClient)
            return;

        if (!CanRevalidate(user, target, tool, option))
            return;

        var time = legacyTiming ? GetBaseTime(option) : GetOperationTime(option, user, target);
        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(time),
            new SurgicalOperationDoAfterEvent
            {
                Operation = option.Operation.ID,
                TargetEntity = GetNetEntity(target),
                Organ = option.Organ is { } organ ? GetNetEntity(organ) : null,
                Inserting = option.Inserting,
                HealMode = option.HealMode,
                UsedTool = tool is { } used ? GetNetEntity(used) : null,
            },
            eventTarget: target,
            target: target,
            used: tool)
        {
            BreakOnMove = true,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
            NeedHand = true,
            BreakOnHandChange = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        if (tool is { } heldTool && TryComp(heldTool, out SurgeryToolComponent? toolComp) && toolComp.StartSound != null)
            _audio.PlayEntity(toolComp.StartSound, user, heldTool);

        var popup = option.Organ is { } chosenOrgan
            ? Loc.GetString(option.Inserting ? "surgery-popup-begin-insert-organ" : "surgery-popup-begin-remove-organ",
                ("user", user), ("target", target), ("organ", Name(chosenOrgan)))
            : Loc.GetString("surgery-popup-begin-operation", ("user", user), ("target", target), ("operation", option.Name));

        _popup.PopupEntity(popup, user, PopupType.Medium);
    }

    private bool CanRevalidate(EntityUid user, EntityUid target, EntityUid? tool, SurgicalOperationOption option)
    {
        // A stale option is never trusted: recompute the whole availability list.
        foreach (var candidate in GetAvailableOperations(user, target, tool))
        {
            if (candidate.Key == option.Key)
                return true;
        }

        return false;
    }

    private float GetOperationTime(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        var baseTime = GetBaseTime(option);
        var speed = GetSpeedModifier(option, user, target);
        return MathF.Max(0.1f, baseTime * MathF.Min(speed, FailureThreshold));
    }

    private float GetBaseTime(SurgicalOperationOption option)
    {
        var baseTime = option.Operation.Time;
        if (baseTime <= 0f && option.Operation.Step is { } stepId && GetSingleton(stepId) is { } step &&
            TryComp(step, out SurgeryStepComponent? stepComp))
        {
            baseTime = stepComp.Duration;
        }

        if (baseTime <= 0f)
            baseTime = option.Operation.FallbackTime;

        return baseTime;
    }

    private float GetSpeedModifier(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        var speed = 1f;

        var tool = _handsSystem.GetActiveItem(user);
        if (TryMatchTool(option.Operation, tool, out var toolSpeed, out _))
            speed *= toolSpeed;

        if (HasComp<SurgerySpeedModifierComponent>(user))
            speed /= Comp<SurgerySpeedModifierComponent>(user).SpeedModifier;

        // Operating tables are the intended venue; a plain floor is twice as slow.
        speed *= GetLocationModifier(target);

        // Using a self-surgery trait on an operation that does not normally allow it is slower.
        if (user == target && !option.Operation.Flags.HasFlag(SurgicalOperationFlags.SelfOperable))
            speed *= 1.5f;

        return MathF.Max(0.05f, speed);
    }

    private float GetLocationModifier(EntityUid target)
    {
        if (TryComp(target, out Content.Shared.Buckle.Components.BuckleComponent? buckle) &&
            HasComp<OperatingTableComponent>(buckle.BuckledTo))
        {
            return 1f;
        }

        if (_autodoc.OnOperatingPlatform(target))
            return 0.85f;

        return 2f;
    }

    private void OnOperationDoAfter(Entity<SurgeryTargetComponent> ent, ref SurgicalOperationDoAfterEvent args) =>
        HandleDoAfter(ent.Owner, ref args);

    private void OnOperationDoAfterPart(Entity<BodyPartComponent> ent, ref SurgicalOperationDoAfterEvent args) =>
        HandleDoAfter(ent.Owner, ref args);

    private void HandleDoAfter(EntityUid eventTarget, ref SurgicalOperationDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("surgery-popup-interrupted"), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        var target = eventTarget;

        if (!_prototypes.TryIndex(args.Operation.Id, out SurgicalOperationPrototype? operation))
            return;

        if (!HasComp<SurgeryTargetComponent>(target) && !HasComp<BodyPartComponent>(target))
            return;

        var tool = args.UsedTool is { } netTool
            ? GetEntity(netTool)
            : _handsSystem.GetActiveItem(args.User);

        if (!TryResolveOption(args.User, target, tool, operation.ID, args.Organ, args.Inserting, args.HealMode,
                out var option))
        {
            _popup.PopupEntity(Loc.GetString("surgery-popup-invalid"), args.User, args.User, PopupType.MediumCaution);
            return;
        }

        option.Tool = tool;

        var speed = GetSpeedModifier(option, args.User, target);
        if (args.User == target && !option.Operation.Flags.HasFlag(SurgicalOperationFlags.SelfOperable))
            speed += 1.5f;

        if (args.User == target && TryComp<SurgerySpeedModifierComponent>(args.User, out var _))
            speed = MathF.Max(speed, 1f);

        var baseTime = GetBaseTime(option);
        var failureChance = Math.Clamp(FailureChancePerSecond * (speed - FailureThreshold) * baseTime, 0f, 99f);

        if (_random.Prob(failureChance / 100f))
        {
            ApplyFailure(option, args.User, target);
            return;
        }

        ApplySuccess(option, args.User, target);

        if (option.Operation.Flags.HasFlag(SurgicalOperationFlags.Looping) && CanRepeat(option, args.User, target, tool))
            args.Repeat = true;
    }

    private bool CanRepeat(SurgicalOperationOption option, EntityUid user, EntityUid target, EntityUid? tool)
    {
        if (!CanRevalidate(user, target, tool, option))
            return false;

        if (option.Operation.Step is { } stepId &&
            GetSingleton(option.Operation.Surgery) is { } surgery &&
            IsLegacyStepComplete(target, option.Part, stepId, surgery))
        {
            return false;
        }

        return true;
    }

    private void ApplySuccess(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        switch (option.Operation.Kind)
        {
            case SurgicalOperationKind.OrganManipulation:
                ApplyOrganManipulation(option, user, target);
                break;
            case SurgicalOperationKind.CavityRemoval:
                ApplyCavityRemoval(option, user, target);
                break;
            case SurgicalOperationKind.ImplantRemoval:
                ApplyImplantRemoval(option, user, target);
                break;
            default:
                ApplyDefault(option, user, target);
                break;
        }

        _sites.Recalculate(option.Part);

        if (option.Operation.Step is { } step &&
            GetSingleton(option.Operation.Surgery) is { } surgery &&
            TryComp<SurgeryComponent>(surgery, out var surgeryComp))
        {
            _wolfmedConditions.RecordStep(option.Part, (surgery, surgeryComp), step);
        }

        var message = option.Organ is { } organ
            ? Loc.GetString(option.Inserting ? "surgery-popup-success-insert-organ" : "surgery-popup-success-remove-organ",
                ("user", user), ("target", target), ("organ", Name(organ)))
            : Loc.GetString("surgery-popup-success", ("user", user), ("target", target), ("operation", option.Name));

        _popup.PopupEntity(message, user, PopupType.Medium);
    }

    private void ApplyDefault(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        var surgeryId = option.Operation.Surgery;
        if (GetSingleton(surgeryId) is not { } surgery)
            return;

        ApplyLegacyStep(user, target, option.Part, option, surgery);
    }

    private void ApplyOrganManipulation(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        var part = option.Part;
        if (!TryComp<BodyPartComponent>(part, out var partComp))
            return;

        if (option.Inserting)
        {
            var organ = _handsSystem.GetActiveItem(user) ?? option.Organ;
            if (organ is not { } held || !TryComp<OrganComponent>(held, out var organComp))
                return;

            _body.InsertOrgan(part, held, organComp.SlotId, partComp, organComp);
            _body.TrySetOrganUsed(held, true, organComp);
            ApplyStepDamage(option, user, target);
            return;
        }

        if (option.Organ is not { } removed)
            return;

        if (!_body.RemoveOrgan(removed))
            return;

        _handsSystem.TryPickupAnyHand(user, removed);
        ApplyStepDamage(option, user, target);
    }

    /// <summary>Applies the step's authored damage or healing effects after a direct effect handler.</summary>
    private void ApplyStepDamage(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        if (option.Operation.Step is not { } stepId || GetSingleton(stepId) is not { } step)
            return;

        var ev = new SurgeryStepDamageChangeEvent(user, target, option.Part, step);
        RaiseLocalEvent(step, ref ev);
    }

    private void ApplyImplantRemoval(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        if (!TryComp<Content.Shared.Implants.Components.ImplantedComponent>(target, out var implanted) ||
            implanted.ImplantContainer.ContainedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("surgery-popup-no-implant"), user, PopupType.MediumCaution);
            return;
        }

        var implant = implanted.ImplantContainer.ContainedEntities[0];
        _implants.ForceRemove(target, implant);

        if (!_handsSystem.TryPickupAnyHand(user, implant))
            _transform.SetCoordinates(implant, Transform(target).Coordinates);
    }

    private void ApplyCavityRemoval(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        if (!TryComp<BodyPartComponent>(option.Part, out var partComp))
            return;

        if (!_itemSlots.TryGetSlot(option.Part, partComp.ContainerName, out var slot))
            return;

        _itemSlots.TryEjectToHands(option.Part, slot, user);
        _sites.SetState(option.Part, SurgeryState.CavityWidened, false);
    }

    private void ApplyFailure(SurgicalOperationOption option, EntityUid user, EntityUid target)
    {
        var part = option.Part;
        if (TryComp(part, out BodyPartComponent? partComp) && HasComp<DamageableComponent>(target))
        {
            var damage = new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Blunt"), option.Operation.FailureDamage);
            _damageable.TryChangeDamage(target, damage, true, origin: user, canSever: false,
                targetPart: _body.GetTargetBodyPart(partComp));

            if (option.Operation.FailureBloodloss > 0f)
            {
                var blood = new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Bloodloss"),
                    option.Operation.FailureBloodloss);
                _damageable.TryChangeDamage(target, blood, true, origin: user, canSever: false);
            }
        }

        _popup.PopupEntity(Loc.GetString("surgery-popup-failure", ("user", user), ("target", target),
            ("operation", option.Name)), user, PopupType.MediumCaution);
    }
}
