using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>A grid's standing rule for when its NPC gunners open fire. Without one they fire on any ship marked hostile.</summary>
[RegisterComponent]
public sealed partial class WFGunnerPolicyComponent : Component
{
    /// <summary>When the ship's gunners open fire.</summary>
    [DataField]
    public WFGunnerPolicy Policy = WFGunnerPolicy.Hostiles;
}
