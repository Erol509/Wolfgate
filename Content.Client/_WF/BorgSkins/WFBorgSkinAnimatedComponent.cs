namespace Content.Client._WF.BorgSkins;

/// <summary>Marks a borg with a Wolfgate skin, so <see cref="WFBorgSkinSystem"/> poses and animates it each frame.</summary>
[RegisterComponent]
public sealed partial class WFBorgSkinAnimatedComponent : Component
{
    /// <summary>Swap the chassis layers to their <c>_moving</c> states while the borg moves.</summary>
    [ViewVariables]
    public bool Walks;

    /// <summary>The body layer is showing a pose state, to be undone when the pose ends.</summary>
    [ViewVariables]
    public bool Posed;
}
