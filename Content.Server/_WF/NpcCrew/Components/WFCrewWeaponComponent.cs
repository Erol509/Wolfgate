namespace Content.Server._WF.NpcCrew.Components;

/// <summary>The weapon a crewman has drawn and the equipment slot it goes back into.</summary>
[RegisterComponent]
public sealed partial class WFCrewWeaponComponent : Component
{
    [ViewVariables]
    public EntityUid? Drawn;

    [ViewVariables]
    public string? HolsterSlot;
}
