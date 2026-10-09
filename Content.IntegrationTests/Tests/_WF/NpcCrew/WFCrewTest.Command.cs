#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>External hull threats trigger a moving orbit instead of a stationary hold.</summary>
    [Test]
    public async Task CaptainEvadesShipAttack()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var attacker = await CreateDeck(new Vector2(300, 0), 3, gravity: true);
        EntityUid pilot = default, helm = default;
        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(deck);
            var crew = Server.System<WFCrewSystem>();
            var captain = crew.SpawnCrewman(WFCrewRoles.Captain, new EntityCoordinates(deck, Vector2.One), "evade")!.Value;
            pilot = crew.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "evade")!.Value;
            SEntMan.GetComponent<HTNComponent>(captain).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            helm = SEntMan.SpawnAtPosition(TestHelm, new EntityCoordinates(deck, new Vector2(3.5f)));
        });
        await WaitUntil(() => Server.System<WFPilotDutySystem>().TryFindHelm(pilot, out var available) && available == helm,
            120, () => DescribePilot(pilot, helm));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WFPilotDutySystem>().TryTakeHelm(pilot, helm), Is.True, DescribePilot(pilot, helm));
            Server.System<WFCrewAlertSystem>().ReportShipThreat(deck, "evade", attacker);
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(WFPilotOrder.Loiter));
            Assert.That(duty.LoiterCenter!.Value.EntityId, Is.EqualTo(attacker));
            Assert.That(SEntMan.GetComponent<Content.Server._Mono.NPC.HTN.ShipSteererComponent>(pilot).AvoidProjectiles, Is.True);
        });
    }

    /// <summary>Real artillery impacts alert a crew once, while same-ship fire does not.</summary>
    [Test]
    public async Task HullFireAlertsCrewAndRadio()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var attacker = await CreateDeck(new Vector2(20, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WFCrewSystem>();
            var alerts = Server.System<WFCrewAlertSystem>();
            var radio = crew.SpawnCrewman(WFCrewRoles.RadioOperator,
                new EntityCoordinates(deck, new Vector2(1.5f)), "hull")!.Value;
            SEntMan.GetComponent<HTNComponent>(radio).Enabled = false;
            var hull = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(2.5f)));
            var gun = SEntMan.SpawnEntity(null, new EntityCoordinates(deck, Vector2.One));
            var shot = SEntMan.SpawnEntity(null, new EntityCoordinates(deck, Vector2.One));
            SEntMan.EnsureComponent<ShipWeaponProjectileComponent>(shot);
            SEntMan.EnsureComponent<ProjectileComponent>(shot).Weapon = gun;
            var hit = new ProjectileHitEvent(new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } }, hull);
            SEntMan.EventBus.RaiseLocalEvent(shot, ref hit);
            Assert.That(alerts.IsAlerted(deck, "hull"), Is.False);
            Server.System<SharedTransformSystem>().SetCoordinates(gun, new EntityCoordinates(attacker, Vector2.One));
            SEntMan.EventBus.RaiseLocalEvent(shot, ref hit);
            SEntMan.EventBus.RaiseLocalEvent(shot, ref hit);
            Assert.That(alerts.IsAlerted(deck, "hull"), Is.True);
            Assert.That(alerts.GetHostileShips(deck, "hull"), Is.EquivalentTo(new[] { attacker }));
            Assert.That(SEntMan.GetComponent<WFRadioOperatorComponent>(radio).Sent.Count(line => line.Line == WFRadioLine.Mayday && line.Channel.Id == "Common"), Is.EqualTo(1));
        });
        await RunTicks(3700);
        await Server.WaitAssertion(() => Assert.That(Server.System<WFCrewAlertSystem>().IsAlerted(deck, "hull"), Is.False));
    }

    /// <summary>Captains resume the remaining route, but never replace orders issued during an alert.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CaptainHoldsAndResumesCourse(bool overrideOrders)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WFCrewSystem>();
            var pilots = Server.System<WFPilotDutySystem>();
            var captain = crew.SpawnCrewman(WFCrewRoles.Captain, new EntityCoordinates(deck, Vector2.One), "bridge")!.Value;
            var pilot = crew.SpawnCrewman(WFCrewRoles.Pilot, new EntityCoordinates(deck, new Vector2(2, 1)), "bridge")!.Value;
            SEntMan.GetComponent<HTNComponent>(captain).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(pilot).Enabled = false;
            var destination = new EntityCoordinates(deck, new Vector2(100, 100));
            pilots.GoTo(pilot, new() { new EntityCoordinates(deck, Vector2.Zero), destination });
            SEntMan.GetComponent<WFPilotDutyComponent>(pilot).WaypointIndex = 1;
            var alert = new WFCrewAlertEvent(deck, "other", System.Array.Empty<EntityUid>());
            SEntMan.EventBus.RaiseLocalEvent(deck, ref alert, true);
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilot).Orders, Is.EqualTo(WFPilotOrder.GoTo));
            alert = new WFCrewAlertEvent(deck, "bridge", System.Array.Empty<EntityUid>());
            SEntMan.EventBus.RaiseLocalEvent(deck, ref alert, true);
            Assert.That(SEntMan.GetComponent<WFPilotDutyComponent>(pilot).Orders, Is.EqualTo(WFPilotOrder.Hold));
            if (overrideOrders)
                pilots.Loiter(pilot, destination, 80);
            var clear = new WFCrewAlertClearedEvent(deck, "bridge");
            SEntMan.EventBus.RaiseLocalEvent(deck, ref clear, true);
            var duty = SEntMan.GetComponent<WFPilotDutyComponent>(pilot);
            Assert.That(duty.Orders, Is.EqualTo(overrideOrders ? WFPilotOrder.Loiter : WFPilotOrder.GoTo));
            if (!overrideOrders)
                Assert.That(duty.Waypoints, Is.EqualTo(new[] { destination }));
        });
    }
}
