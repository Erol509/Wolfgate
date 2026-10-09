#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A queue whose pilot is down hands the helm to a living crewmate instead of waiting on no-pilot.</summary>
    [Test]
    public async Task DownedPilotHandsTheHelmToACrewmate()
    {
        var deck = await CreateDeck(new Vector2(2200, 2000), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var pilot = ConvoyCrew(deck, "WFCrewPilot", "succession", new Vector2(2.5f, 3.5f));
            var deckhand = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(4.5f)), "succession")!.Value;
            var objectives = Server.System<WFCrewObjectiveSystem>();
            Assert.That(objectives.SetQueue(deck, "succession", new()
            {
                new WFCrewObjective { Kind = WFCrewObjectiveKind.Hold, Duration = 100 },
            }), Is.True);
            Server.System<MobStateSystem>().ChangeMobState(pilot, MobState.Dead);
            objectives.Update(.5f);

            Assert.That(objectives.QueueStatus(deck, "succession"), Is.EqualTo("awaiting-helm"));
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(deckhand).Duty, Is.EqualTo(WFCrewDuties.Pilot));
            Assert.That(SEntMan.HasComponent<WFPilotDutyComponent>(deckhand), Is.True);
            Assert.That(SEntMan.GetComponent<WFCrewComponent>(pilot).Duty, Is.EqualTo(WFCrewDuties.Guard),
                "The fallen pilot stands down, so two never share the helm.");
        });
    }

    /// <summary>A travel order that runs past its time is given up as target-lost, for the queue's owner to skip.</summary>
    [Test]
    public async Task OverdueTravelOrderReportsTargetLost()
    {
        var ship = await PoweredNavigationShip(new Vector2(500, 500));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFCrewObjectiveSystem>().SetQueue(ship.Grid, "navigation", new()
            {
                new WFCrewObjective { Kind = WFCrewObjectiveKind.GoTo, Position = new Vector2(4500, 500), Range = 10, Duration = 2 },
            }), Is.True);
        });
        await WaitUntil(() => Server.System<WFCrewObjectiveSystem>().QueueStatus(ship.Grid, "navigation") == "target-lost",
            600, () => DescribePilot(ship.Pilot, ship.Helm));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(ship.Pilot).Orders, Is.EqualTo(WFPilotOrder.Hold));
            Assert.That(Server.System<WFCrewObjectiveSystem>().Snapshot().Single(row => row.Group == "navigation").Objectives,
                Has.Count.EqualTo(1), "The overdue order waits for its owner to skip or retry it.");
        });
    }

    /// <summary>Letting go of the helm puts the hull back on dampening instead of the steering's coasting setting.</summary>
    [Test]
    public async Task ReleasedHelmRestoresDampening()
    {
        var ship = await PoweredNavigationShip(new Vector2(800, 500));
        await Server.WaitAssertion(() =>
        {
            var shuttles = Server.System<ShuttleSystem>();
            shuttles.SetInertiaDampening(ship.Grid, SEntMan.GetComponent<PhysicsComponent>(ship.Grid),
                SEntMan.GetComponent<ShuttleComponent>(ship.Grid), SEntMan.GetComponent<TransformComponent>(ship.Grid),
                InertiaDampeningMode.Off);
            Assert.That(shuttles.NfGetInertiaDampeningMode(ship.Grid), Is.EqualTo(InertiaDampeningMode.Off));

            Server.System<WFPilotDutySystem>().ReleaseHelm(ship.Pilot);
            Assert.That(shuttles.NfGetInertiaDampeningMode(ship.Grid), Is.EqualTo(InertiaDampeningMode.Dampen));
        });
    }
}
