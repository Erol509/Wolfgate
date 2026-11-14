using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Maps a research technology to the surgical operations it unlocks. Keyed by the technology's id.
/// </summary>
[Prototype("surgicalResearch")]
public sealed partial class SurgicalResearchPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Operations unlocked when the technology is researched.</summary>
    [DataField]
    public List<ProtoId<SurgicalOperationPrototype>> Operations = new();
}
