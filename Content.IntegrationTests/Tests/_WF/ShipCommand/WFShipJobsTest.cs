#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.CryoSleep;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.ShipCommand;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.ShipCommand;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipCommand;

/// <summary>
/// The Lupercal's player jobs: all closed when it spawns, so it stays off the join list; opening one lists it. A
/// player who takes the pilot's job relieves the NPC pilot and stands as an officer; cryosleep hands the post back to
/// the roster, takes them off the officers and reopens the slot. The dockmaster's job takes over the dockmaster's
/// counter, whose trader leaves at once.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipJobsSystem))]
public sealed class WFShipJobsTest
{
    [Test]
    public async Task JobsClosedRelieveAndHandBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var shipJobs = entMan.System<WFShipJobsSystem>();
        var stationJobs = entMan.System<StationJobsSystem>();
        var stations = entMan.System<StationSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        EntityUid grid = default, station = default, pilotNpc = default, dockmaster = default;
        WFShipRosterSlot pilotSlot = default!, counterSlot = default!;
        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(500, 500), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
            station = stations.GetOwningStation(grid)!.Value;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var jobs = entMan.GetComponent<WFShipJobsComponent>(grid);
            var listed = entMan.GetComponent<StationJobsComponent>(station);
            Assert.That(shipJobs.IsJobsStation(station), Is.True);
            Assert.That(shipJobs.GetSlots((grid, jobs)).Values.All(open => open == 0), Is.True, "Every job starts closed.");
            Assert.That(shipJobs.ListedForJoin(station, listed), Is.False, "With every job closed the ship stays off the join list.");
            Assert.That(jobs.Jobs.ContainsKey("WFLupercalAi") && jobs.Jobs.ContainsKey("WFLupercalCyborg"), Is.True, "The AI and cyborg jobs are there, closed.");

            Assert.That(shipJobs.SetSlots((grid, jobs), "WFLupercalPilot", 1, null), Is.True);
            Assert.That(shipJobs.ListedForJoin(station, listed), Is.True, "One open job lists the ship.");

            var roster = entMan.GetComponent<WFShipRosterComponent>(grid);
            pilotSlot = roster.Slots.Values.Single(s => s.Kind == WFShipRosterKind.Crew && s.Role.Id == "WFCrewPilot");
            counterSlot = roster.Slots.Values.Single(s => s.Kind == WFShipRosterKind.Counter && s.Trader?.Id == "WFLupercalDockmaster");
            pilotNpc = pilotSlot.Occupant!.Value;
            dockmaster = counterSlot.Occupant!.Value;
        });

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", new Robust.Shared.Map.EntityCoordinates(grid, Vector2.Zero));
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            Assert.That(stationJobs.TryAssignJob(station, "WFLupercalPilot", session.UserId), Is.True);
            var ev = new PlayerSpawnCompleteEvent(body, session, "WFLupercalPilot", true, true, 1, station, HumanoidCharacterProfile.DefaultWithSpecies());
            entMan.EventBus.RaiseLocalEvent(body, ev, true);
        });

        await server.WaitAssertion(() =>
        {
            var command = entMan.GetComponent<WFShipCommandComponent>(grid);
            Assert.That(pilotSlot.Holder, Is.EqualTo(body), "The player holds the pilot's post.");
            Assert.That(entMan.HasComponent<WFShipRelievedComponent>(pilotNpc), Is.True, "The NPC pilot is relieved.");
            Assert.That(command.Officers.Any(p => p.User == session.UserId), Is.True, "The pilot stands as an officer.");
            stationJobs.TryGetJobSlot(station, "WFLupercalPilot", out var open);
            Assert.That(open, Is.EqualTo(0), "The slot was taken.");

            entMan.EventBus.RaiseLocalEvent(body, new CryosleepEnterEvent(body, session.UserId), true);
            Assert.That(pilotSlot.Holder, Is.Null, "Cryosleep hands the post back.");
            Assert.That(pilotSlot.Down, Is.Not.Null, "The roster restaffs it by its usual rule.");
            Assert.That(command.Officers.Any(p => p.User == session.UserId), Is.False, "They are off the officers.");
            Assert.That(entMan.HasComponent<WFShipJobHolderComponent>(body), Is.False);
            stationJobs.TryGetJobSlot(station, "WFLupercalPilot", out open);
            Assert.That(open, Is.EqualTo(1), "The slot reopens.");
        });

        // The dockmaster's job takes over his counter; the trader goes at once.
        EntityUid other = default;
        await server.WaitPost(() =>
        {
            var jobs = entMan.GetComponent<WFShipJobsComponent>(grid);
            Assert.That(shipJobs.SetSlots((grid, jobs), "WFLupercalDockmaster", 1, null), Is.True);
            other = entMan.SpawnEntity("MobHuman", new Robust.Shared.Map.EntityCoordinates(grid, Vector2.Zero));
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, other);
            var ev = new PlayerSpawnCompleteEvent(other, session, "WFLupercalDockmaster", true, true, 2, station, HumanoidCharacterProfile.DefaultWithSpecies());
            entMan.EventBus.RaiseLocalEvent(other, ev, true);
            Assert.That(counterSlot.Holder, Is.EqualTo(other));
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(dockmaster), Is.True, "The dockmaster closes his counter and leaves.");

            var jobs = entMan.GetComponent<WFShipJobsComponent>(grid);
            Assert.That(shipJobs.SetSlots((grid, jobs), "WFLupercalDockmaster", 0, null), Is.True);
            entMan.EventBus.RaiseLocalEvent(other, new CryosleepEnterEvent(other, session.UserId), true);
            stationJobs.TryGetJobSlot(station, "WFLupercalDockmaster", out var open);
            Assert.That(open, Is.EqualTo(0), "A job the owner closed stays closed when its holder leaves.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A job only raises a standing (the pre-PR audit): a barred player is turned away from the jobs and keeps the bar
    /// if they get in; an officer on a Staff job stays an officer; someone a job raised gets their old standing back
    /// when they leave it.
    /// </summary>
    [Test]
    public async Task JobsNeverLiftABarOrLowerAStanding()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var shipJobs = entMan.System<WFShipJobsSystem>();
        var command = entMan.System<WFShipCommandSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        EntityUid grid = default, station = default;
        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(entMan.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(500, 500), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
            station = entMan.System<StationSystem>().GetOwningStation(grid)!.Value;
            var jobs = entMan.GetComponent<WFShipJobsComponent>(grid);
            shipJobs.SetSlots((grid, jobs), "WFLupercalMarine", 3, null);
            shipJobs.SetSlots((grid, jobs), "WFLupercalPilot", 3, null);
        });
        await pair.RunTicksSync(5);

        var n = 10;
        EntityUid Take(string job)
        {
            var body = entMan.SpawnEntity("MobHuman", new Robust.Shared.Map.EntityCoordinates(grid, Vector2.Zero));
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            var ev = new PlayerSpawnCompleteEvent(body, session, job, true, true, n++, station, HumanoidCharacterProfile.DefaultWithSpecies());
            entMan.EventBus.RaiseLocalEvent(body, ev, true);
            return body;
        }

        void Leave(EntityUid body) => entMan.EventBus.RaiseLocalEvent(body, new CryosleepEnterEvent(body, session.UserId), true);

        await server.WaitAssertion(() =>
        {
            var lists = entMan.GetComponent<WFShipCommandComponent>(grid);
            bool On(List<WFShipCommandPerson> list) => list.Any(p => p.User == session.UserId);

            // Barred: turned away from the jobs, and still barred if they get in anyway.
            command.AddPerson((grid, lists), WFShipCommandList.Barred, null, session.UserId, "Barred Player");
            Assert.That(shipJobs.Refuses(station, session.UserId), Is.True, "The ship turns its barred away from its jobs.");
            var marine = Take("WFLupercalMarine");
            Assert.That(On(lists.Barred) && !On(lists.Staff), Is.True, "Taking a job doesn't lift a bar.");
            Leave(marine);
            Assert.That(On(lists.Barred), Is.True, "Nor does leaving it.");
            command.RemovePerson((grid, lists), WFShipCommandList.Barred, null, session.UserId);
            Assert.That(shipJobs.Refuses(station, session.UserId), Is.False);

            // An officer on a Staff job stays an officer, and still is after leaving it.
            command.AddPerson((grid, lists), WFShipCommandList.Officers, null, session.UserId, "Officer Player");
            marine = Take("WFLupercalMarine");
            Assert.That(On(lists.Officers) && !On(lists.Staff), Is.True, "A Staff job doesn't lower an officer.");
            Leave(marine);
            Assert.That(On(lists.Officers), Is.True, "Leaving it keeps them an officer.");
            command.RemovePerson((grid, lists), WFShipCommandList.Officers, null, session.UserId);

            // A staffer the pilot's job raises gets their staff standing back when they leave.
            command.AddPerson((grid, lists), WFShipCommandList.Staff, null, session.UserId, "Staff Player");
            var pilot = Take("WFLupercalPilot");
            Assert.That(On(lists.Officers) && !On(lists.Staff), Is.True, "The pilot's job raises a staffer to officer.");
            Leave(pilot);
            Assert.That(!On(lists.Officers) && On(lists.Staff), Is.True, "Leaving it puts them back on the staff list.");
        });

        await pair.CleanReturnAsync();
    }
}
