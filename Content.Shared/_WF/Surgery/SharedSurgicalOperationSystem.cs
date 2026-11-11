using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// State-driven surgical operations, the Wolfgate equivalent of /tg/station's surgery operation datums.
/// </summary>
/// <remarks>
/// Availability is computed from the target part's <see cref="SurgeryState"/> and the tool the surgeon is
/// holding. Execution reuses the legacy step prototypes as an effect layer, so every existing Wolfgate
/// procedure, wound effect and organ handler keeps working.
/// </remarks>
public abstract partial class SharedSurgicalOperationSystem : EntitySystem
{
    [Dependency] protected IComponentFactory _components = default!;
    [Dependency] protected IConfigurationManager _config = default!;
    [Dependency] protected SharedDoAfterSystem _doAfter = default!;
    [Dependency] protected SharedHandsSystem _hands = default!;
    [Dependency] protected InventorySystem _inventory = default!;
    [Dependency] protected ItemSlotsSystem _itemSlots = default!;
    [Dependency] protected INetManager _net = default!;
    [Dependency] protected SharedPopupSystem _popup = default!;
    [Dependency] protected IPrototypeManager _prototypes = default!;
    [Dependency] protected SharedBodySystem _body = default!;
    [Dependency] protected SharedAutodocSystem _autodoc = default!;
    [Dependency] protected StandingStateSystem _standing = default!;
    [Dependency] protected SharedToolSystem _tools = default!;
    [Dependency] protected SurgicalSiteSystem _sites = default!;

    private readonly Dictionary<EntProtoId, EntityUid> _singletons = new();
    private readonly List<SurgicalOperationPrototype> _operations = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        CacheOperations();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<SurgicalOperationPrototype>())
            CacheOperations();
    }

    private void CacheOperations()
    {
        _operations.Clear();
        foreach (var operation in _prototypes.EnumeratePrototypes<SurgicalOperationPrototype>())
            _operations.Add(operation);

        _operations.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    /// <summary>Whether an operation has been unlocked by research. Always true for unlocked operations.</summary>
    protected virtual bool IsOperationUnlocked(SurgicalOperationPrototype operation) =>
        !operation.Flags.HasFlag(SurgicalOperationFlags.Locked);

    /// <summary>All operations currently offered by the surgeon's tool and the target's state.</summary>
    public List<SurgicalOperationOption> GetAvailableOperations(EntityUid user, EntityUid target, EntityUid? tool)
    {
        var results = new List<SurgicalOperationOption>();
        var isPart = HasComp<BodyPartComponent>(target);
        var body = isPart ? EntityUid.Invalid : target;

        if (!isPart)
        {
            if (!TryComp<SurgeryTargetComponent>(target, out var surgeryTarget) || !surgeryTarget.CanOperate)
                return results;

            if (!HasComp<BodyComponent>(target))
                return results;
        }

        var zone = GetSelectedZone(user);

        foreach (var operation in _operations)
        {
            if (operation.Flags.HasFlag(SurgicalOperationFlags.Locked) && !IsOperationUnlocked(operation))
                continue;

            if (!TryGetTargetPart(user, target, operation, zone, out var part))
                continue;

            if (!CheckAvailability(user, body, part, operation, tool, zone, isPart))
                continue;

            if (!TryGetOptions(operation, user, body, part, tool, out var options))
                continue;

            results.AddRange(options);
        }

        return results;
    }

    /// <summary>Revalidates one specific choice; this is what the server runs before starting a do-after.</summary>
    public bool TryResolveOption(
        EntityUid user,
        EntityUid target,
        EntityUid? tool,
        EntProtoId operationId,
        NetEntity? organ,
        bool inserting,
        SurgicalHealMode healMode,
        out SurgicalOperationOption option)
    {
        option = default!;
        if (!_prototypes.TryIndex(operationId, out SurgicalOperationPrototype? prototype))
            return false;

        foreach (var candidate in GetAvailableOperations(user, target, tool))
        {
            if (candidate.Operation != prototype)
                continue;

            if (candidate.Inserting != inserting || candidate.HealMode != healMode)
                continue;

            if (organ is { } netOrgan)
            {
                if (candidate.Organ is not { } candidateOrgan || GetNetEntity(candidateOrgan) != netOrgan)
                    continue;
            }
            else if (candidate.Organ != null)
            {
                continue;
            }

            option = candidate;
            return true;
        }

        return false;
    }

    /// <summary>Looks up the part an operation would work on for the surgeon's selected zone.</summary>
    public bool TryGetTargetPart(
        EntityUid user,
        EntityUid target,
        SurgicalOperationPrototype operation,
        TargetBodyPart zone,
        out EntityUid part)
    {
        part = EntityUid.Invalid;

        if (HasComp<BodyPartComponent>(target))
        {
            if (!operation.Flags.HasFlag(SurgicalOperationFlags.NoPatient))
                return false;

            part = target;
            return true;
        }

        var type = operation.PartType ?? ZoneToPartType(zone);
        var symmetry = operation.Symmetry ?? (operation.PartType == null ? ZoneToSymmetry(zone) : null);

        foreach (var (id, bodyPart) in _body.GetBodyChildren(target))
        {
            if (bodyPart.PartType != type)
                continue;
            if (symmetry is { } wanted && bodyPart.Symmetry != wanted)
                continue;

            part = id;
            return true;
        }

        return false;
    }

    private bool CheckAvailability(
        EntityUid user,
        EntityUid body,
        EntityUid part,
        SurgicalOperationPrototype operation,
        EntityUid? tool,
        TargetBodyPart zone,
        bool detached,
        bool requireTool = true)
    {
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical) && !_sites.IsMechanical(part))
            return false;
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Organic) && _sites.IsMechanical(part))
            return false;

        if (operation.Zones is { } zones && (zones & zone) == 0)
            return false;

        var state = _sites.GetEffectiveState(part);
        if (!state.HasAll(operation.RequiredStates))
            return false;
        if (operation.AnyRequiredStates != SurgeryState.None && !state.HasAny(operation.AnyRequiredStates))
            return false;
        if (state.HasAny(operation.BlockedStates))
            return false;

        if (requireTool && !TryMatchTool(operation, tool, out _, out _))
            return false;

        if (!detached)
        {
            if (user == body && !operation.Flags.HasFlag(SurgicalOperationFlags.SelfOperable) &&
                !_config.GetCVar(CCVars.CanOperateOnSelf))
            {
                return false;
            }

            if (!operation.Flags.HasFlag(SurgicalOperationFlags.StandingAllowed) && !IsLyingDown(body))
                return false;

            if (!operation.Flags.HasFlag(SurgicalOperationFlags.IgnoreClothes) && !IsSiteAccessible(body, part))
                return false;
        }

        if (GetSingleton(operation.Surgery) is not { } surgeryEnt ||
            !TryComp(surgeryEnt, out SurgeryComponent? surgery))
        {
            return false;
        }

        var valid = new SurgeryValidEvent(body, part);
        RaiseLocalEvent(surgeryEnt, ref valid);
        if (valid.Cancelled)
            return false;

        // WOLFGATE(Genitals): a surgery may refuse a particular surgeon (adult content and consent).
        var userValid = new Content.Shared._WF.Genitals.SurgeryUserValidEvent(user, body, part);
        RaiseLocalEvent(surgeryEnt, ref userValid);
        if (userValid.Cancelled)
            return false;

        EntityUid? stepEnt = null;
        if (operation.Step is { } stepId)
        {
            stepEnt = GetSingleton(stepId);
            if (stepEnt is not { } step)
                return false;

            RaiseLocalEvent(step, ref valid);
            if (valid.Cancelled)
                return false;

            if (operation.RequirePreviousSteps && !operation.Flags.HasFlag(SurgicalOperationFlags.IgnoreStepOrder) &&
                !PreviousLegacyStepsComplete(body, part, (surgeryEnt, surgery), stepId))
            {
                return false;
            }

            if (!CheckStepCanPerform(user, body, part, step, tool))
                return false;
        }

        return true;
    }

    /// <summary>Runs the legacy step's own pre-flight checks (operating table, armor, extra tool handlers).</summary>
    private bool CheckStepCanPerform(EntityUid user, EntityUid body, EntityUid part, EntityUid step, EntityUid? tool)
    {
        var tools = GetToolList(user, tool);
        var can = new SurgeryCanPerformStepEvent(user, body, tools, GetSlotFlags(part))
        {
            ToolValidated = true,
        };

        if (tool is { } used)
        {
            can.ValidTools ??= new Dictionary<EntityUid, float>();
            can.ValidTools[used] = 1f;
        }

        RaiseLocalEvent(step, ref can);
        return can.Invalid == StepInvalidReason.None;
    }

    private bool TryGetOptions(
        SurgicalOperationPrototype operation,
        EntityUid user,
        EntityUid body,
        EntityUid part,
        EntityUid? tool,
        out List<SurgicalOperationOption> options)
    {
        options = new List<SurgicalOperationOption>();

        switch (operation.Kind)
        {
            case SurgicalOperationKind.OrganManipulation:
                return TryGetOrganOptions(operation, user, body, part, tool, options);
            case SurgicalOperationKind.TendWounds:
                return TryGetTendOptions(operation, part, options);
            case SurgicalOperationKind.CavityRemoval:
                return TryGetCavityOptions(operation, part, options);
            default:
                options.Add(MakeOption(operation, part, tool));
                return true;
        }
    }

    private SurgicalOperationOption MakeOption(
        SurgicalOperationPrototype operation,
        EntityUid part,
        EntityUid? tool,
        EntityUid? organ = null,
        bool inserting = false,
        SurgicalHealMode healMode = SurgicalHealMode.Combo,
        string? name = null,
        string? description = null)
    {
        TryMatchTool(operation, tool, out _, out _);

        return new SurgicalOperationOption
        {
            Operation = operation,
            Part = part,
            Organ = organ,
            Inserting = inserting,
            HealMode = healMode,
            Name = name ?? Loc.GetString(operation.Name),
            Description = description ?? Loc.GetString(operation.Description),
            ToolName = operation.RecommendedTool is { } rec ? Loc.GetString(rec) : GetRecommendedToolName(operation),
            Icon = operation.Icon ?? GetStepIcon(operation),
            Priority = operation.Flags.HasFlag(SurgicalOperationFlags.PriorityNextStep),
        };
    }

    private bool TryGetOrganOptions(
        SurgicalOperationPrototype operation,
        EntityUid user,
        EntityUid body,
        EntityUid part,
        EntityUid? tool,
        List<SurgicalOperationOption> options)
    {
        if (!HasComp<BodyPartComponent>(part))
            return false;

        var found = false;
        foreach (var (organId, organ) in _body.GetPartOrgans(part))
        {
            if (!MatchesOrganFilter(operation, organId))
                continue;

            // WOLFGATE(Genitals): anatomy organs are gated by their own per-organ surgeries and consent
            // conditions; the catch-all manipulation operation must not bypass them.
            if (operation.OrganComponents.Count == 0 &&
                HasComp<Content.Shared._WF.Genitals.Components.GenitalOrganComponent>(organId))
            {
                continue;
            }
            if (!organ.Removable)
                continue;
            if (!OrganZoneAllowed(operation, body, part, organId, organ))
                continue;

            var name = Loc.GetString("surgery-option-remove-organ", ("organ", Name(organId)));
            var desc = Loc.GetString("surgery-option-remove-organ-desc", ("organ", Name(organId)));
            options.Add(MakeOption(operation, part, tool, organId, name: name, description: desc));
            found = true;
        }

        if (tool is { } held &&
            TryComp(held, out OrganComponent? heldOrgan) &&
            MatchesOrganFilter(operation, held) &&
            (operation.OrganComponents.Count > 0 ||
             !HasComp<Content.Shared._WF.Genitals.Components.GenitalOrganComponent>(held)) &&
            heldOrgan.Body == null &&
            !OrganSlotOccupied(part, heldOrgan.SlotId) &&
            HasOrganSlot(part, heldOrgan.SlotId) &&
            (!operation.BonesLockChestOrgans || CanReachChestOrgans(part)))
        {
            var name = Loc.GetString("surgery-option-insert-organ", ("organ", Name(held)));
            var desc = Loc.GetString("surgery-option-insert-organ-desc", ("organ", Name(held)));
            options.Add(MakeOption(operation, part, tool, inserting: true, name: name, description: desc));
            found = true;
        }

        return found;
    }

    private bool TryGetTendOptions(SurgicalOperationPrototype operation, EntityUid part, List<SurgicalOperationOption> options)
    {
        // The legacy wounded condition already decided there is something to treat.
        if (operation.HealCombo)
        {
            options.Add(MakeOption(operation, part, null, healMode: SurgicalHealMode.Combo,
                name: Loc.GetString("surgery-option-tend-both"),
                description: Loc.GetString("surgery-option-tend-both-desc")));
        }

        if (operation.HealBrute)
        {
            options.Add(MakeOption(operation, part, null, healMode: SurgicalHealMode.Brute,
                name: Loc.GetString("surgery-option-tend-brute"),
                description: Loc.GetString("surgery-option-tend-brute-desc")));
        }

        if (operation.HealBurn)
        {
            options.Add(MakeOption(operation, part, null, healMode: SurgicalHealMode.Burn,
                name: Loc.GetString("surgery-option-tend-burn"),
                description: Loc.GetString("surgery-option-tend-burn-desc")));
        }

        return options.Count > 0;
    }

    private bool TryGetCavityOptions(SurgicalOperationPrototype operation, EntityUid part, List<SurgicalOperationOption> options)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart) ||
            !_itemSlots.TryGetSlot(part, bodyPart.ContainerName, out var slot) ||
            !slot.HasItem)
        {
            return false;
        }

        var item = slot.Item!.Value;
        options.Add(MakeOption(operation, part, null,
            name: Loc.GetString("surgery-option-remove-cavity-item", ("item", Name(item))),
            description: Loc.GetString("surgery-option-remove-cavity-item-desc", ("item", Name(item)))));
        return true;
    }

    private bool MatchesOrganFilter(SurgicalOperationPrototype operation, EntityUid organ)
    {
        if (operation.OrganSlots.Count > 0 &&
            (!TryComp<OrganComponent>(organ, out var organComp) || !operation.OrganSlots.Contains(organComp.SlotId)))
        {
            return false;
        }

        if (operation.OrganComponents.Count == 0)
            return true;

        foreach (var component in operation.OrganComponents)
        {
            if (_components.TryGetRegistration(component, out var registration) && HasComp(organ, registration.Type))
                return true;
        }

        return false;
    }

    private bool OrganZoneAllowed(
        SurgicalOperationPrototype operation,
        EntityUid body,
        EntityUid part,
        EntityUid organ,
        OrganComponent organComp)
    {
        if (operation.BonesLockChestOrgans && IsChestOrgan(organ, organComp) && !_sites.HasAllStates(part, SurgeryState.BoneSawed))
            return false;

        return true;
    }

    private static bool IsChestOrgan(EntityUid organ, OrganComponent organComp)
    {
        // Brain and the five torso organs need the ribs opened; limbs and external features do not.
        return organComp.SlotId is "heart" or "lungs" or "stomach" or "liver" or "kidneys" or "brain";
    }

    private bool CanReachChestOrgans(EntityUid part)
    {
        return !_sites.HasBones(part) || _sites.HasAllStates(part, SurgeryState.BoneSawed);
    }

    private bool HasOrganSlot(EntityUid part, string slotId) =>
        TryComp<BodyPartComponent>(part, out var bodyPart) && bodyPart.Organs.ContainsKey(slotId);

    private bool OrganSlotOccupied(EntityUid part, string slotId)
    {
        foreach (var (_, organ) in _body.GetPartOrgans(part))
        {
            if (organ.SlotId == slotId)
                return true;
        }

        return false;
    }

    /// <summary>Matches the operation's tool list against the held item. Hands are considered when listed.</summary>
    public bool TryMatchTool(SurgicalOperationPrototype operation, EntityUid? tool, out float speed, out string? toolName)
    {
        speed = 1f;
        toolName = null;

        if (operation.Tools.Count == 0)
        {
            if (operation.RecommendedTool is { } recommended)
                toolName = Loc.GetString(recommended);
            return true;
        }

        foreach (var entry in operation.Tools)
        {
            if (entry.Hand && tool == null)
            {
                speed = entry.Speed;
                toolName = Loc.GetString("surgery-tool-hands");
                return true;
            }

            if (tool is not { } used)
                continue;

            var matches = entry.AnyItem;

            if (!matches && entry.Component != null &&
                _components.TryGetRegistration(entry.Component, out var registration) &&
                HasComp(used, registration.Type))
            {
                matches = true;
            }

            if (!matches && entry.Quality is { } quality &&
                _tools.HasQuality(used, quality.Id))
            {
                matches = true;
            }

            if (!matches && (entry.RequiresSharp || entry.MinForce > 0f) &&
                TryComp<Content.Shared.Weapons.Melee.MeleeWeaponComponent>(used, out var melee))
            {
                var slash = melee.Damage.DamageDict.GetValueOrDefault("Slash");
                var piercing = melee.Damage.DamageDict.GetValueOrDefault("Piercing");
                var force = melee.Damage.GetTotal().Float();
                matches = (slash > 0 || piercing > 0) && force >= entry.MinForce;
            }

            if (!matches)
                continue;

            speed = entry.Speed;
            toolName = GetEntryToolName(entry, used);
            return true;
        }

        return false;
    }

    private string? GetEntryToolName(SurgicalToolEntry entry, EntityUid tool)
    {
        foreach (var component in EntityManager.GetComponents(tool))
        {
            if (component is Content.Shared._Shitmed.Medical.Surgery.Tools.ISurgeryToolComponent surgeryTool)
                return Loc.GetString(surgeryTool.ToolName);
        }

        if (entry.Quality is { } quality && _prototypes.TryIndex(quality, out ToolQualityPrototype? qualityProto))
            return Loc.GetString(qualityProto.ToolName);

        return Name(tool);
    }

    /// <summary>Operations the operating computer can list, whether or not they are currently available.</summary>
    public IEnumerable<SurgicalOperationPrototype> AllOperations => _operations;

    /// <summary>The tool name the operation wants, for the operating computer.</summary>
    public string GetRecommendedToolName(SurgicalOperationPrototype operation)
    {
        if (operation.RecommendedTool is { } recommended)
            return Loc.GetString(recommended);

        if (operation.Tools.Count == 0)
            return string.Empty;

        var entry = operation.Tools[0];
        if (entry.Hand)
            return Loc.GetString("surgery-tool-hands");
        if (entry.Quality is { } quality && _prototypes.TryIndex(quality, out ToolQualityPrototype? qualityProto))
            return Loc.GetString(qualityProto.ToolName);
        if (entry.Component != null && _components.TryGetRegistration(entry.Component, out var registration))
        {
            if (_components.GetComponent(registration.Type) is Content.Shared._Shitmed.Medical.Surgery.Tools.ISurgeryToolComponent surgeryTool)
                return surgeryTool.ToolName;
        }

        return entry.Component ?? string.Empty;
    }

    /// <summary>Whether the operation would currently be offered to a surgeon with the passed tool.</summary>
    public bool IsOperationRecommended(EntityUid user, EntityUid target, SurgicalOperationPrototype operation)
    {
        var tool = _hands.GetActiveItem(user);
        foreach (var option in GetAvailableOperations(user, target, tool))
        {
            if (option.Operation == operation)
                return true;
        }

        // Availability is tool-dependent; the computer only asks which operations the state permits.
        foreach (var option in GetAvailableOperationsIgnoringTools(user, target))
        {
            if (option.Operation == operation)
                return true;
        }

        return false;
    }

    /// <summary>State-only availability, used by the operating computer to recommend next steps.</summary>
    public List<SurgicalOperationOption> GetAvailableOperationsIgnoringTools(EntityUid user, EntityUid target)
    {
        var results = new List<SurgicalOperationOption>();
        if (!TryComp<SurgeryTargetComponent>(target, out var surgeryTarget) || !surgeryTarget.CanOperate)
            return results;

        var zone = GetSelectedZone(user);
        foreach (var operation in _operations)
        {
            if (operation.Flags.HasFlag(SurgicalOperationFlags.Locked) && !IsOperationUnlocked(operation))
                continue;

            if (!TryGetTargetPart(user, target, operation, zone, out var part))
                continue;

            if (!CheckAvailability(user, target, part, operation, null, zone, false, requireTool: false))
                continue;

            if (!TryGetOptions(operation, user, target, part, null, out var options))
                continue;

            results.AddRange(options);
        }

        return results;
    }

    /// <summary>All operations whose state requirements are met, for the operating computer's catalog.</summary>
    public bool IsStateAvailable(EntityUid patient, EntityUid part, SurgicalOperationPrototype operation)
    {
        if (!TryComp<SurgicalSiteComponent>(part, out _))
            return false;

        if (operation.Flags.HasFlag(SurgicalOperationFlags.Locked) && !IsOperationUnlocked(operation))
            return false;

        var state = GetEffectiveStateSafe(part);
        if (!state.HasAll(operation.RequiredStates))
            return false;
        if (operation.AnyRequiredStates != SurgeryState.None && !state.HasAny(operation.AnyRequiredStates))
            return false;
        if (state.HasAny(operation.BlockedStates))
            return false;
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Mechanical) && !_sites.IsMechanical(part))
            return false;
        if (operation.Flags.HasFlag(SurgicalOperationFlags.Organic) && _sites.IsMechanical(part))
            return false;

        if (GetSingleton(operation.Surgery) is not { } surgeryEnt)
            return false;

        var ev = new SurgeryValidEvent(patient, part);
        RaiseLocalEvent(surgeryEnt, ref ev);
        return !ev.Cancelled;
    }

    private SurgeryState GetEffectiveStateSafe(EntityUid part) => _sites.GetEffectiveState(part);

    /// <summary>The currently selected body zone of a surgeon.</summary>
    public TargetBodyPart GetSelectedZone(EntityUid user)
    {
        return TryComp<TargetingComponent>(user, out var targeting) ? targeting.Target : TargetBodyPart.Torso;
    }

    /// <summary>Maps a target zone to a body part type.</summary>
    public static BodyPartType ZoneToPartType(TargetBodyPart zone)
    {
        return zone switch
        {
            TargetBodyPart.Head => BodyPartType.Head,
            TargetBodyPart.Torso or TargetBodyPart.Groin => BodyPartType.Torso,
            TargetBodyPart.LeftArm or TargetBodyPart.RightArm => BodyPartType.Arm,
            TargetBodyPart.LeftHand or TargetBodyPart.RightHand => BodyPartType.Hand,
            TargetBodyPart.LeftLeg or TargetBodyPart.RightLeg => BodyPartType.Leg,
            TargetBodyPart.LeftFoot or TargetBodyPart.RightFoot => BodyPartType.Foot,
            _ => BodyPartType.Torso,
        };
    }

    /// <summary>Maps a target zone to a body part symmetry, or null for central parts.</summary>
    public static BodyPartSymmetry? ZoneToSymmetry(TargetBodyPart zone)
    {
        return zone switch
        {
            TargetBodyPart.LeftArm or TargetBodyPart.LeftHand or TargetBodyPart.LeftLeg or TargetBodyPart.LeftFoot =>
                BodyPartSymmetry.Left,
            TargetBodyPart.RightArm or TargetBodyPart.RightHand or TargetBodyPart.RightLeg or TargetBodyPart.RightFoot =>
                BodyPartSymmetry.Right,
            _ => null,
        };
    }

    /// <summary>The slot flags clothing must leave empty for the part to be reachable.</summary>
    public SlotFlags GetSlotFlags(EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart))
            return SlotFlags.NONE;

        return bodyPart.PartType switch
        {
            BodyPartType.Head => SlotFlags.HEAD,
            BodyPartType.Torso => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Arm => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Hand => SlotFlags.GLOVES,
            BodyPartType.Leg => SlotFlags.OUTERCLOTHING | SlotFlags.LEGS,
            BodyPartType.Foot => SlotFlags.FEET,
            _ => SlotFlags.NONE,
        };
    }

    /// <summary>Whether clothing leaves the operation site exposed.</summary>
    public bool IsSiteAccessible(EntityUid body, EntityUid part)
    {
        if (_inventory.TryGetContainerSlotEnumerator(body, out var slots, GetSlotFlags(part)))
        {
            while (slots.NextItem(out _))
                return false;
        }

        return true;
    }

    /// <summary>Whether the patient is lying down, buckled sideways, or on an operating platform.</summary>
    public bool IsLyingDown(EntityUid body)
    {
        if (_standing.IsDown(body) || _autodoc.OnOperatingPlatform(body))
            return true;

        if (TryComp(body, out BuckleComponent? buckle) &&
            TryComp(buckle.BuckledTo, out StrapComponent? strap))
        {
            var rotation = strap.Rotation;
            if (rotation.GetCardinalDir() is Direction.West or Direction.East)
                return true;
        }

        return false;
    }

    /// <summary>Held tools with the used item first, for legacy step handlers.</summary>
    public List<EntityUid> GetToolList(EntityUid user, EntityUid? used)
    {
        var tools = new List<EntityUid>();
        if (used is { } usedTool)
            tools.Add(usedTool);

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != used)
                tools.Add(held);
        }

        return tools;
    }

    /// <summary>A cached singleton of a legacy surgery or step prototype, with its conditions.</summary>
    public EntityUid? GetSingleton(EntProtoId id)
    {
        if (!_prototypes.HasIndex<EntityPrototype>(id))
            return null;

        if (_singletons.TryGetValue(id, out var ent) && !TerminatingOrDeleted(ent))
            return ent;

        ent = Spawn(id, MapCoordinates.Nullspace);
        _singletons[id] = ent;
        return ent;
    }

    /// <summary>Whether every earlier step of a legacy surgery reads as complete.</summary>
    public bool PreviousLegacyStepsComplete(
        EntityUid body,
        EntityUid part,
        Entity<SurgeryComponent> surgery,
        EntProtoId stepId)
    {
        if (surgery.Comp.Requirement is { } requirementId &&
            GetSingleton(requirementId) is { } requirementEnt &&
            TryComp(requirementEnt, out SurgeryComponent? requiredComp) &&
            !PreviousLegacyStepsComplete(body, part, (requirementEnt, requiredComp), stepId))
        {
            return false;
        }

        foreach (var surgeryStep in surgery.Comp.Steps)
        {
            if (surgeryStep == stepId)
                break;

            if (!IsLegacyStepComplete(body, part, surgeryStep, surgery.Owner))
                return false;
        }

        return true;
    }

    /// <summary>Whether a legacy step's own completion check passes.</summary>
    public bool IsLegacyStepComplete(EntityUid body, EntityUid part, EntProtoId stepId, EntityUid surgery)
    {
        if (GetSingleton(stepId) is not { } stepEnt)
            return false;

        var ev = new SurgeryStepCompleteCheckEvent(body, part, surgery);
        RaiseLocalEvent(stepEnt, ref ev);
        return !ev.Cancelled;
    }

    private SpriteSpecifier? GetStepIcon(SurgicalOperationPrototype operation)
    {
        // Icons live on the client's SpriteComponent, so prototypes that want a specific icon declare it.
        return operation.Icon;
    }

    /// <summary>Localised name of any entity.</summary>
    public string NameOf(EntityUid uid) => Name(uid);

    /// <summary>Runs the effect of a completed operation; server only.</summary>
    protected void ApplyLegacyStep(
        EntityUid user,
        EntityUid body,
        EntityUid part,
        SurgicalOperationOption option,
        EntityUid surgeryEnt)
    {
        if (option.Operation.Step is not { } stepId || GetSingleton(stepId) is not { } step)
            return;

        var tools = GetToolList(user, option.Tool);
        var ev = new SurgeryStepEvent(user, body, part, tools, surgeryEnt)
        {
            // The state-driven engine already accepted the tool, including ghetto substitutes.
            ToolValidated = true,
        };

        // The operation may have been reached with a quality-based substitute; tell the step it is fine.
        RaiseLocalEvent(step, ref ev);

        // YAML damage/heal effects on the step are part of its authored result.
        var damage = new SurgeryStepDamageChangeEvent(user, body, part, step);
        RaiseLocalEvent(step, ref damage);
    }
}
