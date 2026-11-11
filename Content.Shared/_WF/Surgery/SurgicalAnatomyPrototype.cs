using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Species anatomy as surgery sees it. Keyed by the body part's species id; species without an entry are
/// treated as having skin, bones and blood vessels.
/// </summary>
[Prototype("surgicalAnatomy")]
public sealed partial class SurgicalAnatomyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Whether the species has skin that must be incised.</summary>
    [DataField]
    public bool HasSkin = true;

    /// <summary>Whether the species has bones that must be sawed.</summary>
    [DataField]
    public bool HasBones = true;

    /// <summary>Whether the species has blood vessels that must be clamped.</summary>
    [DataField]
    public bool HasVessels = true;
}
