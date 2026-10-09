#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Power.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Radio.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>Judging an encounter that begins beside another, stranded ships, and who speaks and in what colour.</summary>
public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string BatchCorePrototypes = @"
- type: wfEncounter
  id: WFTestCoreRunning
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: lone
    vessel: WFDredger
    objectives:
    - kind: Hold
      duration: 600

- type: wfEncounter
  id: WFTestCoreWaiting
  name: wf-encounter-name-convoy
  start: Manual
  startRadius: 300
  ships:
  - key: lone
    vessel: WFDredger
    objectives:
    - kind: Hold
      duration: 600

- type: wfEncounter
  id: WFTestCoreStranded
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: dry
    vessel: WFDredger
    side: stranded
    stranded: Fuel
    objectives:
    - kind: GoTo
      offset: 2000, 0
  - key: wrecked
    vessel: WFDredger
    offset: 300, 0
    side: stranded
    stranded: Thrusters
    objectives:
    - kind: GoTo
      offset: 2000, 0

- type: wfEncounter
  id: WFTestCoreTwoSides
  name: wf-encounter-name-convoy
  start: Manual
  announcement: wf-encounter-announce-backup
  announceOnRadio: true
  announcer: caller
  ships:
  - key: raider
    vessel: WFDredger
    side: raiders
    captain: false
    deckhands: 0
    roles: [ WFCrewPilot ]
  - key: caller
    vessel: WFDredger
    offset: 300, 0
    side: callers
    captain: false
    deckhands: 0
    roles: [ WFCrewPilot ]
    iffColor: ""#123456""
";

    /// <summary>
    /// An encounter that begins while another is running is judged on the orders it was just given, not on crews read
    /// before it had any: its finite hold is not taken for flown.
    /// </summary>
    [Test]
    public async Task EncounterBegunBesideAnotherIsNotCompletedAtOnce()
    {
        EntityUid running = default, waiting = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestCoreRunning"),
                new MapCoordinates(new Vector2(31000, 31000), MapData.MapId), out running), Is.True);
            Assert.That(system.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestCoreWaiting"),
                new MapCoordinates(new Vector2(33000, 33000), MapData.MapId), out waiting), Is.True);
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player),
                new EntityCoordinates(MapData.MapUid, new Vector2(33100, 33000)));
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WFEncounterComponent>(waiting).Begun, 900);
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(waiting);
            Assert.That(comp.Ships["lone"].HasOrders, Is.True);
            Assert.That(comp.Ships["lone"].Flown, Is.False);
            Assert.That(comp.Resolution, Is.Null, "Its hold has only begun.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(running).Resolution, Is.Null);

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            var system = Server.System<WFEncounterSystem>();
            system.End(waiting);
            system.End(running);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A ship stranded for fuel arrives with its generators empty and batteries flat and is not topped up; one with
    /// wrecked thrusters has none left. Both hold their orders back until they are rescued.
    /// </summary>
    [Test]
    public async Task StrandedShipsArriveWithoutFuelOrThrustersAndWait()
    {
        EntityUid encounter = default, dry = default, wrecked = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestCoreStranded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(35000, 35000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            dry = comp.Ships["dry"].Grid;
            wrecked = comp.Ships["wrecked"].Grid;
            Assert.That(comp.Ships["dry"].Stranding, Is.EqualTo(WFEncounterStranding.Fuel));
            Assert.That(comp.Ships["wrecked"].Stranding, Is.EqualTo(WFEncounterStranding.Thrusters));

            var batteries = SEntMan.EntityQueryEnumerator<PowerNetworkBatteryComponent, BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out _, out _, out var battery, out var xform))
            {
                if (xform.GridUid == dry)
                    Assert.That(battery.CurrentCharge, Is.Zero, "The batteries are flat.");
            }
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var generators = Server.System<GeneratorSystem>();
            var found = 0;
            var fuelled = SEntMan.EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
            while (fuelled.MoveNext(out var uid, out var generator, out var xform))
            {
                if (xform.GridUid != dry)
                    continue;

                found++;
                Assert.That(generators.GetFuel(uid), Is.Zero, "The generator is empty and nobody refuelled it.");
                Assert.That(generator.On, Is.False);
            }

            Assert.That(found, Is.GreaterThan(0));
            var thrusters = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusters.MoveNext(out _, out var thruster, out var xform))
            {
                Assert.That(xform.GridUid == wrecked && thruster.Type == ThrusterType.Linear, Is.False, "No thruster drives the wrecked ship.");
            }

            foreach (var ship in comp.Ships.Values)
            {
                Assert.That(WFEncounterSystem.IsStranded(ship), Is.True);
                Assert.That(ship.HasOrders, Is.False, "Its orders wait for the rescue.");
            }

            Assert.That(comp.Resolution, Is.Null, "A stranded ship is not out of the encounter for being unable to move.");
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// Only the announcing ship voices the announcement, though another is listed first; each side shows its radar
    /// colour, its own where one is set.
    /// </summary>
    [Test]
    public async Task AnnouncerSpeaksAndSidesShowTheirColors()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestCoreTwoSides");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(37000, 37000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var raider = comp.Ships["raider"];
            var caller = comp.Ships["caller"];

            var callerSpoke = false;
            var members = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == raider.Group)
                    Assert.That(SEntMan.HasComponent<TelecomExemptComponent>(uid), Is.False, "The raider does not speak for the encounter.");
                else if (member.Group == caller.Group && SEntMan.HasComponent<TelecomExemptComponent>(uid))
                    callerSpoke = true;
            }

            Assert.That(callerSpoke, Is.True, "The caller made the announcement.");
            Assert.That(SEntMan.GetComponent<IFFComponent>(raider.Grid).Color, Is.EqualTo(WFEncounterSystem.SideColors[0]));
            Assert.That(SEntMan.GetComponent<IFFComponent>(caller.Grid).Color, Is.EqualTo(Color.FromHex("#123456")));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
