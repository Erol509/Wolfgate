#nullable enable
using System;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.ShipAccess;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipCommand;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.ShipCommand;

/// <summary>
/// The ship's NPC crew judge visitors through ShipCommand: by zone and standing, with the zone's response for those
/// turned away; the owner and admins are never targets; attackers and the barred are turned away anywhere.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipCommandSystem))]
public sealed class WFShipCrewTest
{
    [Test]
    public async Task CrewJudgeByZoneAndStanding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var security = entMan.System<WFCrewSecuritySystem>();
        var command = entMan.System<WFShipCommandSystem>();
        var keys = entMan.System<WFShipAccessSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            // Tile (0, 0) is the test map's one tile; the Staff zone covers it, and a Public deck is anywhere else.
            entMan.EnsureComponent<WFShipAccessComponent>(grid);
            var cmd = entMan.EnsureComponent<WFShipCommandComponent>(grid);
            var zones = entMan.EnsureComponent<WFShipZonesComponent>(grid);
            zones.Zones.Add(new WFShipZone { Id = "core", Name = "wf-ship-zone-test", Mode = WFShipZoneMode.Staff, Areas = { new Box2i(0, 0, 0, 0) } });

            var crew = entMan.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand, map.GridCoords, "lupercal")!.Value;
            entMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var rules = entMan.EnsureComponent<WFCrewSecurityComponent>(crew);
            rules.Boarding = WFCrewSecurityResponse.Ignore;

            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            var (staffer, staffCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Cy Lund", 3);
            var (stranger, strangerCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 4);
            keys.TryGetKey(staffCard, out var staffKey);
            keys.TryGetKey(strangerCard, out var strangerKey);
            command.AddPerson((grid, cmd), WFShipCommandList.Staff, staffKey, null, "Cy Lund");

            Assert.Multiple(() =>
            {
                Assert.That(security.IsAuthorized(crew, owner), Is.True, "The owner is welcome everywhere.");
                Assert.That(security.IsAuthorized(crew, staffer), Is.True, "Staff are welcome in a Staff zone.");
                Assert.That(security.IsAuthorized(crew, stranger), Is.False, "A stranger is not welcome in a Staff zone.");
                Assert.That(security.BoardingResponse(crew, stranger, rules), Is.EqualTo(WFCrewSecurityResponse.Warn),
                    "The zone's response replaces the crewman's own boarding rule.");
            });

            // Out on the open deck, off every zone, the stranger is a guest.
            xforms.SetCoordinates(stranger, new EntityCoordinates(grid, new Vector2(5.5f, 5.5f)));
            Assert.That(security.IsAuthorized(crew, stranger), Is.True, "The open deck is public.");

            // Locked: staff are turned away too, with the Locked response.
            cmd.LockedZoneResponse = WFCrewSecurityResponse.Hostile;
            command.SetZoneMode(grid, "core", WFShipZoneMode.Locked);
            Assert.That(security.IsAuthorized(crew, staffer), Is.False, "Staff may not stand in a Locked zone.");
            Assert.That(security.BoardingResponse(crew, staffer, rules), Is.EqualTo(WFCrewSecurityResponse.Hostile));

            // Barred: turned away even on the open deck.
            command.AddPerson((grid, cmd), WFShipCommandList.Barred, strangerKey, null, "Random Stranger");
            Assert.That(security.IsAuthorized(crew, stranger), Is.False, "The barred are turned away anywhere.");
            Assert.That(security.BoardingResponse(crew, stranger, rules), Is.EqualTo(cmd.BarredResponse));

            // An attacker loses the welcome, whatever his standing.
            command.SetZoneMode(grid, "core", WFShipZoneMode.Public);
            Assert.That(security.IsAuthorized(crew, staffer), Is.True, "Precondition: the zone is public.");
            var memories = entMan.EnsureComponent<NPCRetaliationComponent>(crew).AttackMemories;
            memories[staffer] = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(1);
            Assert.That(security.IsAuthorized(crew, staffer), Is.False, "Someone who attacked the crew is not welcome.");
            Assert.That(security.BoardingResponse(crew, staffer, rules), Is.EqualTo(WFCrewSecurityResponse.Hostile));

            // Docking ships are welcome while the ship says so.
            var other = map.Grid.Owner;
            Assert.That(security.IsAuthorized(crew, other), Is.True, "A docking ship is welcome.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crew's own card opens its ship's Staff and Locked zone doors, so the crew walk them.</summary>
    [Test]
    public async Task CrewWalkTheirZones()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var command = entMan.System<WFShipCommandSystem>();
        var readers = entMan.System<Content.Shared.Access.Systems.AccessReaderSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(grid);
            entMan.EnsureComponent<WFShipCommandComponent>(grid);
            var zones = entMan.EnsureComponent<WFShipZonesComponent>(grid);
            zones.Zones.Add(new WFShipZone { Id = "core", Name = "wf-ship-zone-test", Mode = WFShipZoneMode.Public });
            var door = entMan.SpawnEntity("AirlockShuttle", map.GridCoords);
            entMan.RemoveComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(door);
            entMan.EnsureComponent<WFDoorAccessRuleComponent>(door).Zone = "core";

            var crew = entMan.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand, map.GridCoords, "lupercal")!.Value;
            entMan.GetComponent<HTNComponent>(crew).Enabled = false;
            Assert.That(entMan.HasComponent<WFShipAccessComponent>(grid), Is.True, "Precondition: the crew's spawn set the ship's access up.");

            command.SetZoneMode(grid, "core", WFShipZoneMode.Locked);
            command.RefreshZoneKeys(grid);
            Assert.That(readers.IsAllowed(crew, door), Is.True, "The crew's own card opens a Locked zone door.");
        });

        await pair.CleanReturnAsync();
    }
}
