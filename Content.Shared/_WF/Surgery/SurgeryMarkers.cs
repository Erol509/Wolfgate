using Robust.Shared.GameStates;

namespace Content.Shared._WF.Surgery;

/// <summary>Holes have been drilled into the part's bone.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BoneDrilledComponent : Component;

/// <summary>Plastic has been applied to the part during plastic surgery.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class PlasticAppliedComponent : Component;

/// <summary>An arbitrary prosthetic is attached to the part but not yet secured.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ProstheticUnsecuredComponent : Component;

/// <summary>The part's chest cavity has been widened to accept items.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CavityWidenedComponent : Component;
