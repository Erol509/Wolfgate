#nullable enable
using System.Numerics;
using System.Threading;
using Content.Server._WF.NpcCrew.HTN;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A combat plan cannot start movement toward an attacker lost after target selection.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewCombatTargetRechecksAfterPlanning(bool deleted)
    {
        var deck = await CreateDeck(new Vector2(20, 20), 9, true);
        EntityUid member = default, attacker = default;
        await Server.WaitPost(() =>
        {
            var crew = Server.System<WFCrewSystem>();
            member = crew.SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "target-lifetime")!.Value;
            crew.SetEngagement(member, WFCrewEngagement.OnSight);
            var htn = SEntMan.GetComponent<HTNComponent>(member);
            htn.Enabled = false;
            attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(4.5f, 2.5f)));
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var htn = SEntMan.GetComponent<HTNComponent>(member);
            htn.Blackboard.SetValue(NPCBlackboard.Owner, member);
            var operation = new WFCrewTargetOperator();
            operation.Initialize(Server.ResolveDependency<IEntitySystemManager>());
            var (valid, effects) = operation.Plan(htn.Blackboard, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(valid, Is.True);
            Assert.That(effects, Is.Not.Null);
            foreach (var (key, value) in effects!)
                htn.Blackboard.SetValue(key, value);
            Assert.That(operation.Update(htn.Blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Finished));

            if (deleted)
                SEntMan.DeleteEntity(attacker);
            else
                Server.System<MobStateSystem>().ChangeMobState(attacker, MobState.Dead);
            Assert.That(operation.Update(htn.Blackboard, 0f), Is.EqualTo(HTNOperatorStatus.Failed),
                "A stale plan must fail before native movement resolves the attacker's coordinates.");
        });
    }
}
