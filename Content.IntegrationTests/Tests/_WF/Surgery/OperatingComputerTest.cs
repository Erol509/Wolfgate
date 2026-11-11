#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Interaction;
using Content.Server.Research.Systems;
using Content.Shared.Research.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WF.Surgery;

/// <summary>The operating computer tracks the operating table's patient, and research unlocks operations.</summary>
[TestFixture]
[TestOf(typeof(OperatingComputerSystem))]
public sealed class OperatingComputerTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [Test]
    public async Task ComputerTracksTablePatient()
    {
        var map = await CreateTestMap();
        EntityUid computer = default;
        EntityUid table = default;
        EntityUid patient = default;

        await Server.WaitAssertion(() =>
        {
            table = SEntMan.SpawnEntity("OperatingTable", map.GridCoords);
            var computerCoords = map.GridCoords.Offset(new System.Numerics.Vector2(1, 0));
            computer = SEntMan.SpawnEntity("WFOperatingComputer", computerCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.GetComponent<OperatingComputerComponent>(computer).RequiresPower = false;

            Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, table), Is.True,
                "The test patient should buckle to the operating table.");

            var scalpel = SEntMan.SpawnEntity("Scalpel", map.GridCoords);
            var used = new InteractUsingEvent(patient, scalpel, computer,
                SEntMan.GetComponent<TransformComponent>(computer).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(computer, used);
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var component = SEntMan.GetComponent<OperatingComputerComponent>(computer);
            Assert.That(component.Table, Is.EqualTo(table), "The computer should find the adjacent table.");
            Assert.That(component.Patient, Is.EqualTo(patient), "The computer should track the table's occupant.");
        });
    }

    [Test]
    public async Task ResearchUnlocksOperation()
    {
        var map = await CreateTestMap();
        EntityUid database = default;

        await Server.WaitAssertion(() =>
        {
            database = SEntMan.SpawnEntity("Wrench", map.GridCoords);
            SEntMan.EnsureComponent<TechnologyDatabaseComponent>(database);
            SEntMan.System<ResearchSystem>().AddTechnology(database, "WFAdvancedSurgery");
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<SurgicalOperationSystem>().IsUnlocked("WFImplantRemoval"), Is.True,
                "Researching WFAdvancedSurgery should unlock implant removal.");
            Assert.That(SEntMan.System<SurgicalOperationSystem>().IsUnlocked("WFMendSkin"), Is.True,
                "An operation without a research lock stays available.");
        });
    }
}
