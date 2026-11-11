namespace Content.Shared._Shitmed.Medical.Surgery;

/// <summary>
///     Raised on the step entity.
/// </summary>
[ByRefEvent]
public record struct SurgeryStepEvent(EntityUid User, EntityUid Body, EntityUid Part, List<EntityUid> Tools, EntityUid Surgery)
{
    // WOLFGATE(Surgery): caller-validated tools
    // (the engine may use ghetto substitutes, so the step's own list must not veto the effect).
    /// <summary>Set when the caller has already validated the tool for this step.</summary>
    public bool ToolValidated;
}
