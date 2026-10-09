#nullable enable
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Components;
using Robust.Shared.Physics.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Inventory;
using Content.Shared.Prying.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>Navigation opens authorized readers normally, and only pries denied readers when enabled.</summary>
    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task CrewUsesAccessBeforePrying(bool allowed, bool pry)
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
            var crewSystem = Server.System<WFCrewSystem>();
            var readers = Server.System<AccessReaderSystem>();
            Server.System<CrewDoorProbeSystem>();
            EntityUid crew = default, door = default;
            await Server.WaitPost(() =>
            {
                for (var y = 0; y < 9; y++)
                {
                    if (y != 4)
                        SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(4.5f, y + 0.5f)));
                }
                door = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
                SEntMan.GetComponent<DoorComponent>(door).BumpOpen = false;
                SEntMan.EnsureComponent<CrewDoorProbeComponent>(door);
                Assert.That(readers.GetMainAccessReader(door, out var reader), Is.True);
                readers.SetAccesses(reader!.Value, reader.Value.Comp, new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
                crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(2.5f, 4.5f)), "doors")!.Value;
                // This fixture tests tag access and prying, not the automatically issued ship credential.
                Server.System<InventorySystem>().TryUnequip(crew, "id", silent: true, force: true);
                if (allowed)
                    Server.System<SharedAccessSystem>().TrySetTags(crew, new[] { new ProtoId<AccessLevelPrototype>("Engineering") });
                var htn = SEntMan.GetComponent<HTNComponent>(crew);
                htn.Blackboard.SetValue("NavPry", pry);
                htn.Blackboard.SetValue("IdleRange", 0.1f);
                SEntMan.GetComponent<WFCrewComponent>(crew).PostRange = 0.25f;
                crewSystem.SetPost(crew, new EntityCoordinates(deck, new Vector2(6.5f, 4.5f)));
                Assert.That(readers.IsAllowed(crew, door), Is.EqualTo(allowed));
            });

            if (allowed || pry)
                await WaitUntil(() => SEntMan.GetComponent<TransformComponent>(crew).LocalPosition.X > 5.2f,
                    900, () => $"Crew should pass the reader door (access={allowed}, pry={pry}). {DescribeCrewDoor(crew, door)}");
            else
                await RunTicks(300);

            await Server.WaitAssertion(() =>
            {
                var attempts = SEntMan.GetComponent<CrewDoorProbeComponent>(door).PryAttempts;
                if (!allowed && pry)
                    Assert.That(attempts, Is.GreaterThan(0), "Denied crew with NavPry should use ordinary prying.");
                else
                    Assert.That(attempts, Is.Zero, "Authorized crew and crew without NavPry must not pry.");
                if (!allowed && !pry)
                {
                    Assert.That(SEntMan.GetComponent<TransformComponent>(crew).LocalPosition.X, Is.LessThan(4f));
                    Assert.That(SEntMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open));
                }
            });
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }

    private string DescribeCrewDoor(EntityUid crew, EntityUid door)
    {
        var position = SEntMan.GetComponent<TransformComponent>(crew).LocalPosition;
        var state = SEntMan.GetComponent<DoorComponent>(door).State;
        var attempts = SEntMan.GetComponent<CrewDoorProbeComponent>(door).PryAttempts;
        var velocity = SEntMan.GetComponent<PhysicsComponent>(crew).LinearVelocity;
        var path = SEntMan.TryGetComponent<NPCSteeringComponent>(crew, out var steering)
            ? $"flags={steering.Flags} status={steering.Status} nodes={steering.CurrentPath.Count} failures={steering.FailedPathCount} goal={steering.Coordinates} path=[{string.Join(";", steering.CurrentPath.Select(poly => $"{poly.Box}:{poly.Data.Flags}:{poly.Data.IsFreeSpace}"))}]"
            : "no steering";
        return $"pos={position} velocity={velocity} door={state} pries={attempts} {path} {Describe(crew)}";
    }

    /// <summary>Spawned crew carry revocable ship credentials; boarding another ship does not grant access.</summary>
    [Test]
    public async Task CrewShipAccessFollowsItsCard()
    {
        var crewSystem = Server.System<WFCrewSystem>();
        var access = Server.System<WFShipAccessSystem>();
        var ships = Server.System<WFShipAccessServerSystem>();
        var readers = Server.System<AccessReaderSystem>();
        var inventory = Server.System<InventorySystem>();
        var deck = await CreateDeck(new Vector2(6f, 0f), 9, gravity: true);
        var otherDeck = await CreateDeck(new Vector2(20f, 0f), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var ship = SEntMan.EnsureComponent<WFShipAccessComponent>(deck);
            ship.Locked = true;
            var other = SEntMan.EnsureComponent<WFShipAccessComponent>(otherDeck);
            other.Locked = true;
            var door = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(4.5f, 4.5f)));
            var otherDoor = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(otherDeck, new Vector2(2.5f, 2.5f)));
            var crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(3.5f, 4.5f)), "crew")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            ships.RefreshShip((otherDeck, other));
            Assert.That(access.TryGetWornCard(crew, out var card), Is.True);
            Assert.That(access.TryGetKey(card, out var key), Is.True);
            Assert.That(ship.AllowList, Has.Count.EqualTo(1));
            Assert.That(readers.IsAllowed(crew, door), Is.True);
            Assert.That(readers.IsAllowed(crew, otherDoor), Is.False);

            Assert.That(inventory.TryUnequip(crew, "id", silent: true, force: true), Is.True);
            Assert.That(readers.IsAllowed(crew, door), Is.False, "Access is carried by the ID, not the NPC type.");
            Assert.That(inventory.TryEquip(crew, card, "id", silent: true, force: true), Is.True);
            Assert.That(readers.IsAllowed(crew, door), Is.True);
            Assert.That(ships.RemoveEntry((deck, ship), key), Is.True);
            crewSystem.SetDuty(crew, WFCrewDuties.Guard);
            Assert.That(readers.IsAllowed(crew, door), Is.False, "Changing duties must not re-enroll a revoked card.");
            Server.System<SharedTransformSystem>().SetCoordinates(crew,
                new EntityCoordinates(otherDeck, new Vector2(1.5f, 2.5f)));
            crewSystem.SetDuty(crew, WFCrewDuties.Guard);
            Assert.That(other.AllowList, Is.Empty, "Walking aboard another ship must never enroll crew there.");
            Assert.That(readers.IsAllowed(crew, otherDoor), Is.False);
        });
    }
}

/// <summary>Counts pry attempts on a test door.</summary>
[RegisterComponent]
public sealed partial class CrewDoorProbeComponent : Component
{
    public int PryAttempts;
}

/// <summary>Observes ordinary prying without changing its outcome.</summary>
public sealed class CrewDoorProbeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrewDoorProbeComponent, BeforePryEvent>(OnPry);
    }

    private void OnPry(Entity<CrewDoorProbeComponent> ent, ref BeforePryEvent args) => ent.Comp.PryAttempts++;
}
