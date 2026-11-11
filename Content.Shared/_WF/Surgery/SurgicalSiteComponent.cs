using Robust.Shared.GameStates;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Cached anatomical surgical state of a body part. The state itself is derived from the part's surgical
/// marker components; this component exists so the value replicates to the client and can be read cheaply.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgicalSiteComponent : Component
{
    /// <summary>The surgical state currently present on the part.</summary>
    [DataField, AutoNetworkedField]
    public SurgeryState State;

    /// <summary>Override for parts that have no skin to incise. Null derives it from anatomy.</summary>
    [DataField]
    public bool? HasSkin;

    /// <summary>Override for parts that have no bone to saw. Null derives it from anatomy.</summary>
    [DataField]
    public bool? HasBones;

    /// <summary>Override for parts that have no blood vessels to clamp. Null derives it from anatomy.</summary>
    [DataField]
    public bool? HasVessels;
}
