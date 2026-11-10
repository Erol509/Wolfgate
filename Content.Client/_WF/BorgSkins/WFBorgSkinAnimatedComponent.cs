namespace Content.Client._WF.BorgSkins;

/// <summary>Marks a borg whose skin has animated or walking states, so <see cref="WFBorgSkinSystem"/> updates it each frame.</summary>
[RegisterComponent]
public sealed partial class WFBorgSkinAnimatedComponent : Component
{
    /// <summary>Swap the chassis layers to their <c>_moving</c> states while the borg moves.</summary>
    [ViewVariables]
    public bool Walks;
}
