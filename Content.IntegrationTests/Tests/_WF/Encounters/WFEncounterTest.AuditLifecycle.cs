#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string AuditLifecyclePrototypes = @"
- type: wfEncounter
  id: WFTestAuditEscorted
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WFDredger
    side: convoy
    objectives:
    - kind: Hold
      duration: 2
  - key: wing
    vessel: WFDredger
    offset: 200, 0
    side: convoy
    captain: false
    objectives:
    - kind: Escort
      target: lead

- type: wfEncounter
  id: WFTestAuditPersistent
  name: wf-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  cost: 2
  ships:
  - key: lone
    vessel: WFDredger

- type: wfEncounter
  id: WFTestAuditTrader
  name: wf-encounter-name-trader
  start: Manual
  ships:
  - key: trader
    vessel: Piva
    side: trader
    profile: WFCrewProfileHauler
    captain: false
    deckhands: 0
    guards: 1
    roles: [ WFCrewPilot, WFCrewDeckhand ]
    passengers: [ WFTraderWanderer ]
    boarding: Ignore
    docking: Ignore
";

    /// <summary>An encounter completes once its ships with finite orders are done, though an escort never finishes.</summary>
    [Test]
    public async Task EncounterCompletesWhileItsEscortStillFliesFormation()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditEscorted");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(27000, 27000), MapData.MapId), out encounter), Is.True);
        });
        await WaitUntilServer(() => !SEntMan.TryGetComponent(encounter, out WFEncounterComponent? comp) || comp.Resolution != null, 1800);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent(encounter, out WFEncounterComponent? comp), Is.True);
            Assert.That(comp!.Resolution, Is.EqualTo(WFEncounterResolution.Completed), "The lead's hold is done; the wing's escort doesn't hold it up.");
            Assert.That(comp.JumpAt, Is.Not.Null);
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A round-long encounter takes no slot under the cap and nothing from the budget, and sits on the map itself.</summary>
    [Test]
    public async Task PersistentEncounterTakesNoShareOfTheCapOrBudget()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var count = system.ActiveCount();
            var cost = system.ActiveCost();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditPersistent");
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(25000, 25000), MapData.MapId), out var encounter), Is.True);
            Assert.That(system.IsRunning("WFTestAuditPersistent"), Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(count));
            Assert.That(system.ActiveCost(), Is.EqualTo(cost));
            Assert.That(SEntMan.GetComponent<TransformComponent>(encounter).ParentUid, Is.EqualTo(MapData.MapUid),
                "The encounter is never parented to a grid that could take it away.");
            system.End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A trader with no radio officer and no deckhands still calls for help when its passenger is hurt by hand,
    /// through whoever of its crew is aboard.
    /// </summary>
    [Test]
    public async Task TraderWithoutARadioOfficerCallsForHelpWhenItsPassengerIsHurt()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WFEncounterSystem>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditTrader");
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(23000, 23000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var ship = comp.Ships["trader"];

            var crew = new List<(EntityUid Uid, WFCrewComponent Member)>();
            var members = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == ship.Group)
                    crew.Add((uid, member));
            }

            Assert.That(crew, Is.Not.Empty);
            Assert.That(crew.Any(entry => entry.Member.Role == WFCrewRoles.Deckhand), Is.False, "No deckhands asked for, none aboard.");
            Assert.That(crew.Any(entry => SEntMan.HasComponent<WFRadioOperatorComponent>(entry.Uid)), Is.False);

            EntityUid? passenger = null;
            var passengers = SEntMan.EntityQueryEnumerator<WFEncounterPassengerComponent>();
            while (passengers.MoveNext(out var uid, out var aboard))
            {
                if (aboard.Encounter == encounter)
                    passenger = uid;
            }

            Assert.That(passenger, Is.Not.Null);
            Server.System<DamageableSystem>().TryChangeDamage(passenger, new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } },
                origin: SEntMan.GetEntity(Player));
            Assert.That(comp.Category, Is.EqualTo(WFEncounterCategory.Distress), "The mayday went out.");
            Assert.That(ship.NextAttackCall, Is.GreaterThan(STiming.CurTime));
            system.End(encounter);
        });
        await RunTicks(10);
    }
}
