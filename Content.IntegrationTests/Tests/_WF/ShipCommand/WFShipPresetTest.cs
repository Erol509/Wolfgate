#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.ShipCommand;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Inventory;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipCommand;

/// <summary>
/// Personal presets: a Lupercal's settings saved to the database come back unchanged and set up a freshly spawned
/// Lupercal, whose officer, listed by player alone, is recognised in a new body with a new card. Spawn Vessel's
/// default preset path applies the first of its players' presets that exists.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipPresetSystem))]
public sealed class WFShipPresetTest
{
    [Test]
    public async Task PresetRoundTripOntoAFreshShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var command = entMan.System<WFShipCommandSystem>();
        var presets = entMan.System<WFShipPresetSystem>();
        var roster = entMan.System<WFShipRosterSystem>();
        var shipJobs = entMan.System<WFShipJobsSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(500, 0), null, out var a, createStation: true), Is.True);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(-500, 0), null, out var b, createStation: true), Is.True);
            first = a!.Value;
            second = b!.Value;
        });
        await pair.RunTicksSync(5);

        string json = string.Empty, zone = string.Empty, post = string.Empty;
        await server.WaitPost(() =>
        {
            Assert.That(presets.VesselOf(first), Is.EqualTo("WFLupercal"));
            var zones = entMan.GetComponent<WFShipZonesComponent>(first);
            zone = zones.Zones.First(z => z.Mode != WFShipZoneMode.Locked).Id;
            Assert.That(command.SetZoneMode((first, zones), zone, WFShipZoneMode.Locked), Is.True);
            var cmd = entMan.GetComponent<WFShipCommandComponent>(first);
            Assert.That(command.AddPerson((first, cmd), WFShipCommandList.Officers, null, session.UserId, "Tess Arden"), Is.True);
            cmd.AdminsServedFree = false;
            var crew = entMan.GetComponent<WFShipRosterComponent>(first);
            post = crew.Slots.Keys.OrderBy(k => k).First();
            Assert.That(roster.SetStoodDown((first, crew), post, true, null), Is.True);
            roster.SetAutoRestaff((first, crew), true, null);
            Assert.That(shipJobs.SetSlots((first, entMan.GetComponent<WFShipJobsComponent>(first)), "WFLupercalMarine", 2, null), Is.True);
            json = WFShipPresetSystem.Serialize(presets.Capture(first));
        });

        // Through the database and back.
        await db.SaveShipCommandPresetAsync(session.UserId.UserId, "WFLupercal", "test", json);
        var stored = await db.GetShipCommandPresetAsync(session.UserId.UserId, "WFLupercal", "test");
        Assert.That(stored, Is.EqualTo(json), "The preset comes back from the database unchanged.");
        var list = await db.GetShipCommandPresetsAsync(session.UserId.UserId, "WFLupercal");
        Assert.That(list.Select(p => p.Name), Is.EquivalentTo(new[] { "test" }));
        Assert.That(await db.GetShipCommandPresetAsync(new NetUserId(System.Guid.NewGuid()).UserId, "WFLupercal", "test"), Is.Null,
            "Presets are personal: another player sees none.");

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            presets.Apply(second, WFShipPresetSystem.Deserialize(stored!)!, null, "test");

            // The officer was kept by player: a fresh body with a fresh card is still an officer on the fresh ship.
            body = entMan.SpawnEntity("MobHuman", new EntityCoordinates(second, Vector2.Zero));
            var card = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(second, Vector2.Zero));
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(42, second));
            Assert.That(entMan.System<InventorySystem>().TryEquip(body, card, "id", force: true), Is.True);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            // An admin would outrank the officer list.
            var admins = server.ResolveDependency<Content.Server.Administration.Managers.IAdminManager>();
            if (admins.IsAdmin(session))
                admins.DeAdmin(session);
        });

        await server.WaitAssertion(() =>
        {
            var zones = entMan.GetComponent<WFShipZonesComponent>(second);
            var cmd = entMan.GetComponent<WFShipCommandComponent>(second);
            var crew = entMan.GetComponent<WFShipRosterComponent>(second);
            var jobs = entMan.GetComponent<WFShipJobsComponent>(second);
            Assert.Multiple(() =>
            {
                Assert.That(zones.Zones.Single(z => z.Id == zone).Mode, Is.EqualTo(WFShipZoneMode.Locked));
                Assert.That(cmd.Officers.Select(p => p.User), Is.EquivalentTo(new NetUserId?[] { session.UserId }));
                Assert.That(cmd.AdminsServedFree, Is.False);
                Assert.That(crew.AutoRestaff, Is.True);
                Assert.That(crew.Slots[post].StoodDown, Is.True, "The stood-down post is stood down on the fresh ship.");
                Assert.That(shipJobs.GetSlots((second, jobs))["WFLupercalMarine"], Is.EqualTo(2));
                Assert.That(command.GetAuthority(body, second), Is.EqualTo(WFShipAuthority.Officer),
                    "A player listed by account is an officer in a new body with a new card.");
            });
        });

        // Spawn Vessel's path: the first listed player with a preset of the name is the one applied.
        await db.SaveShipCommandPresetAsync(session.UserId.UserId, "WFLupercal", WFShipPresetSystem.DefaultName, json);
        Task<NetUserId?> load = default!;
        await server.WaitPost(() =>
            load = presets.LoadFirst(first, new[] { new NetUserId(System.Guid.NewGuid()), session.UserId }, WFShipPresetSystem.DefaultName, null));
        for (var i = 0; i < 50 && !load.IsCompleted; i++)
            await pair.RunTicksSync(5);
        Assert.That(load.IsCompleted, Is.True);
        Assert.That(await load, Is.EqualTo((NetUserId?) session.UserId), "The owner's default preset is applied when the admin has none.");

        await db.DeleteShipCommandPresetAsync(session.UserId.UserId, "WFLupercal", "test");
        await db.DeleteShipCommandPresetAsync(session.UserId.UserId, "WFLupercal", WFShipPresetSystem.DefaultName);
        await pair.CleanReturnAsync();
    }
}
