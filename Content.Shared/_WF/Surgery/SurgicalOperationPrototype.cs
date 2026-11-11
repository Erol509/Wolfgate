using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Tools;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Surgery;

/// <summary>Behaviour flags for a surgical operation.</summary>
[Flags]
public enum SurgicalOperationFlags : ushort
{
    None = 0,

    /// <summary>The surgeon may perform this on themselves.</summary>
    SelfOperable = 1 << 0,

    /// <summary>The patient does not need to be lying down.</summary>
    StandingAllowed = 1 << 1,

    /// <summary>The operation site may be covered by clothing.</summary>
    IgnoreClothes = 1 << 2,

    /// <summary>Only available on mechanical body parts.</summary>
    Mechanical = 1 << 3,

    /// <summary>Only available on organic body parts.</summary>
    Organic = 1 << 4,

    /// <summary>The operation may be performed on a detached body part.</summary>
    NoPatient = 1 << 5,

    /// <summary>Only available once researched.</summary>
    Locked = 1 << 6,

    /// <summary>Notable operations are logged and remembered.</summary>
    Notable = 1 << 7,

    /// <summary>Shown first as a recommended next step.</summary>
    PriorityNextStep = 1 << 8,

    /// <summary>Cannot be made infallible by traits such as surgical processors.</summary>
    AlwaysFailable = 1 << 9,

    /// <summary>Ignores the legacy step order check, relying purely on surgical state.</summary>
    IgnoreStepOrder = 1 << 10,

    /// <summary>Repeats until no longer available (tend wounds).</summary>
    Looping = 1 << 11,

    /// <summary>Only a robotic surgeon (a chassis) may perform this.</summary>
    Mechanic = 1 << 12,
}

/// <summary>How an operation gathers its choices and applies its effects.</summary>
public enum SurgicalOperationKind : byte
{
    /// <summary>One option, effects come from the referenced legacy step.</summary>
    Default,

    /// <summary>Pick an organ to remove, or insert the held organ.</summary>
    OrganManipulation,

    /// <summary>Pick a bruise or burn treatment; repeats while damage remains.</summary>
    TendWounds,

    /// <summary>Attach the held body part or item to a missing limb slot.</summary>
    ProstheticReplacement,

    /// <summary>Remove the item lodged in the chest cavity.</summary>
    CavityRemoval,

    /// <summary>Search the patient for a subdermal implant and take it out.</summary>
    ImplantRemoval,
}

/// <summary>One accepted tool for an operation.</summary>
[DataDefinition]
public partial struct SurgicalToolEntry
{
    /// <summary>Component name the tool must carry, e.g. <c>Scalpel</c>.</summary>
    [DataField]
    public string? Component;

    /// <summary>Tool quality the tool must carry, e.g. <c>Slicing</c>.</summary>
    [DataField]
    public ProtoId<ToolQualityPrototype>? Quality;

    /// <summary>Accept an empty hand.</summary>
    [DataField]
    public bool Hand;

    /// <summary>Accept any item, including ones with no tool behaviour.</summary>
    [DataField]
    public bool AnyItem;

    /// <summary>The tool must deal Slash or Piercing melee damage (tg's edged sharpness).</summary>
    [DataField]
    public bool RequiresSharp;

    /// <summary>Minimum melee damage total required when <see cref="RequiresSharp"/> is set.</summary>
    [DataField]
    public float MinForce;

    /// <summary>Multiplies the operation time. Values above one are slower.</summary>
    [DataField]
    public float Speed = 1f;
}

/// <summary>
/// A state-gated surgical operation, the equivalent of /tg/station's <c>datum/surgery_operation</c>.
/// </summary>
[Prototype("surgicalOperation")]
public sealed partial class SurgicalOperationPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Short action name shown on the radial and in the computer's next-step list.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Description shown in tooltips and the operation catalog.</summary>
    [DataField]
    public LocId Description = string.Empty;

    /// <summary>Formal name shown in the operation catalog.</summary>
    [DataField]
    public LocId? RndName;

    /// <summary>Formal description shown in the operation catalog.</summary>
    [DataField]
    public LocId? RndDescription;

    /// <summary>Legacy surgery prototype that supplies conditions and progression context.</summary>
    [DataField(required: true)]
    public EntProtoId Surgery;

    /// <summary>Legacy step prototype whose effects run on success.</summary>
    [DataField]
    public EntProtoId? Step;

    /// <summary>Second step used when inserting an organ (organ manipulation only).</summary>
    [DataField]
    public EntProtoId? InsertStep;

    /// <summary>Require the surgery's earlier legacy steps to read as complete.</summary>
    [DataField]
    public bool RequirePreviousSteps = true;

    /// <summary>Base time in seconds. Zero uses the referenced step's duration.</summary>
    [DataField]
    public float Time;

    /// <summary>Time to use when neither the operation nor its step declares one.</summary>
    [DataField]
    public float FallbackTime = 2f;

    /// <summary>All of these states must be present on the target part.</summary>
    [DataField]
    public SurgeryState RequiredStates;

    /// <summary>At least one of these states must be present.</summary>
    [DataField]
    public SurgeryState AnyRequiredStates;

    /// <summary>None of these states may be present.</summary>
    [DataField]
    public SurgeryState BlockedStates;

    /// <summary>Accepted tools, in priority order.</summary>
    [DataField]
    public List<SurgicalToolEntry> Tools = new();

    /// <summary>Tool name shown when the operation is recommended, if the tools are not component based.</summary>
    [DataField]
    public LocId? RecommendedTool;

    /// <summary>Radial icon. Defaults to the referenced step's sprite.</summary>
    [DataField]
    public SpriteSpecifier? Icon;

    /// <summary>Behaviour flags.</summary>
    [DataField]
    public SurgicalOperationFlags Flags;

    /// <summary>Dynamic option and effect strategy.</summary>
    [DataField]
    public SurgicalOperationKind Kind = SurgicalOperationKind.Default;

    /// <summary>When set, the operation targets this part type instead of the selected zone's part.</summary>
    [DataField]
    public BodyPartType? PartType;

    /// <summary>When set with <see cref="PartType"/>, restricts the target to this symmetry.</summary>
    [DataField]
    public BodyPartSymmetry? Symmetry;

    /// <summary>Only offer this operation when one of the selected zones is chosen.</summary>
    [DataField]
    public TargetBodyPart? Zones;

    /// <summary>Priority within the radial and the computer's next-step list; lower comes first.</summary>
    [DataField]
    public int Priority;

    /// <summary>When set, an operation with the same group supersedes this one once available.</summary>
    [DataField]
    public string? Replaces;

    /// <summary>Organ slot whitelist for organ manipulation; empty accepts every organ.</summary>
    [DataField]
    public List<string> OrganSlots = new();

    /// <summary>Organ manipulation: only organs carrying one of these components are offered.</summary>
    [DataField]
    public List<string> OrganComponents = new();

    /// <summary>Only accept external organs during organ manipulation.</summary>
    [DataField]
    public bool ExternalOrgansOnly;

    /// <summary>Only accept internal organs during organ manipulation.</summary>
    [DataField]
    public bool InternalOrgansOnly;

    /// <summary>Organ manipulation: organs in the chest or the brain also need sawed bones.</summary>
    [DataField]
    public bool BonesLockChestOrgans = true;

    /// <summary>Tend wounds: flat healing per pass.</summary>
    [DataField]
    public float HealAmount = 5f;

    /// <summary>Tend wounds: healing as a fraction of remaining damage.</summary>
    [DataField]
    public float HealMultiplier = 0.07f;

    /// <summary>Tend wounds: whether bruises are treated.</summary>
    [DataField]
    public bool HealBrute = true;

    /// <summary>Tend wounds: whether burns are treated.</summary>
    [DataField]
    public bool HealBurn = true;

    /// <summary>Tend wounds: a combined option that treats both at once.</summary>
    [DataField]
    public bool HealCombo = true;

    /// <summary>Damage applied to the operated part when the operation fails.</summary>
    [DataField]
    public float FailureDamage = 5f;

    /// <summary>Blood loss applied when the operation fails.</summary>
    [DataField]
    public float FailureBloodloss = 2f;

    /// <summary>Research technology that unlocks this operation, when locked.</summary>
    [DataField]
    public string? TechUnlock;
}
