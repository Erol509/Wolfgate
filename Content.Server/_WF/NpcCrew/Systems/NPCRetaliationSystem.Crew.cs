using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>Lets a crewman's ship forgive an attacker before upstream retaliation remembers him.</summary>
public sealed partial class NPCRetaliationSystem
{
    [Dependency] private WFCrewSystem _wfCrew = default!;

    /// <summary>Whether this NPC is a crewman whose ship lets the attacker off.</summary>
    private bool WfForgiven(EntityUid uid, EntityUid attacker)
    {
        return HasComp<WFCrewComponent>(uid) && _wfCrew.Forgives(uid, attacker);
    }
}
