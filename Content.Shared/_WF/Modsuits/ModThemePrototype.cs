using Content.Shared.Whitelist;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Modsuits;

[Prototype]
public sealed partial class ModThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LocId? Name;

    [DataField]
    public int ComplexityMax = 15;

    [DataField]
    public float ChargeDrain = 0.8f;

    [DataField]
    public TimeSpan ActivationStepTime = TimeSpan.FromSeconds(1);

    [DataField]
    public List<EntProtoId> InbuiltModules = new();

    [DataField]
    public EntityWhitelist? AllowedModules;

    [DataField]
    public EntityWhitelist? BlacklistedModules;

    /// <summary>Whether a sealed suit projects its hardlight mesh over uncovered body parts.</summary>
    [DataField]
    public bool Hardlight = true;

    [DataField]
    public Color HardlightColor = Color.FromHex("#57C8FF");

    [DataField]
    public Color HardlightColor2 = Color.FromHex("#175A7A");

    /// <summary>Texture sampled for the hardlight pattern; its colours are themed too.</summary>
    [DataField]
    public ResPath HardlightMask = new("/Textures/_WF/Modsuits/Hardlight/standard_blue.png");

    [DataField]
    public float HardlightAlpha = 0.5f;

    [DataField]
    public float HardlightIntensity = 0.8f;
}
