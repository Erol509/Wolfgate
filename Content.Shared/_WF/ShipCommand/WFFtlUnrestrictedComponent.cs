using Robust.Shared.GameStates;

namespace Content.Shared._WF.ShipCommand;

/// <summary>On a ship's grid: lifts the FTL limits an admin vessel shouldn't be held by.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFFtlUnrestrictedComponent : Component
{
    /// <summary>Jump even with powered helms of other ships close by.</summary>
    [DataField, AutoNetworkedField]
    public bool IgnoreProximity = true;

    /// <summary>Jump whatever the grid's mass, past the server's FTL mass limit.</summary>
    [DataField, AutoNetworkedField]
    public bool IgnoreMassLimit = true;

    /// <summary>Jump into no-jump (exclusion) zones.</summary>
    [DataField, AutoNetworkedField]
    public bool IgnoreExclusionZones = true;
}
