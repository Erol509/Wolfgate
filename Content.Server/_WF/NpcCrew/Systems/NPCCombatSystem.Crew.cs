using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    /// <summary>Where a crewman's shot actually goes: the aim point, swung off by up to his skill's error.</summary>
    private EntityCoordinates CrewAim(EntityUid uid, EntityCoordinates target)
    {
        if (!TryComp<WFCrewComponent>(uid, out var crew))
            return target;

        var error = WFCrewSkills.Of(crew.Skill).AimError;
        if (error <= 0f)
            return target;

        var from = _transform.GetMapCoordinates(uid).Position;
        var aim = _transform.ToMapCoordinates(target).Position;
        var missed = from + Angle.FromDegrees(_random.NextFloat(-error, error)).RotateVec(aim - from);
        return _transform.ToCoordinates(target.EntityId, new MapCoordinates(missed, Transform(uid).MapID));
    }
}
