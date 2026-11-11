using Content.Shared.Inventory;

namespace Content.Shared._Shitmed.Medical.Surgery.Steps;

[ByRefEvent]
public record struct SurgeryCanPerformStepEvent(
    EntityUid User,
    EntityUid Body,
    List<EntityUid> Tools,
    SlotFlags TargetSlots,
    string? Popup = null,
    StepInvalidReason Invalid = StepInvalidReason.None,
    Dictionary<EntityUid, float>? ValidTools = null
) : IInventoryRelayEvent
{
    // WOLFGATE(Surgery): caller-validated tools
    // (the engine accepts quality-based substitutes, so the step's own list is already satisfied).
    /// <summary>Set when the caller has already validated the tool for this step.</summary>
    public bool ToolValidated;
}
