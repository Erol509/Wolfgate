#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.Surgery;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._WF.Surgery;
using Content.Shared.Buckle;
using Content.Shared.Interaction;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Surgery;

/// <summary>The operating computer's XAML loads on a real client and the computer opens it.</summary>
[TestFixture]
public sealed class SurgeryUiTest : GameTest
{
    [Test]
    public async Task OperatingComputerWindowLoads()
    {
        await Client.WaitPost(() =>
        {
            var window = new OperatingComputerWindow();
            Assert.That(window.Backdrop, Is.Not.Null);
            window.Dispose();
        });
    }

    [Test]
    public async Task SurgicalRadialLoads()
    {
        await Client.WaitPost(() =>
        {
            var menu = new SurgicalRadialMenu();
            Assert.That(menu.FindControl<Content.Client.UserInterface.Controls.RadialContainer>("Main"), Is.Not.Null);
            menu.Dispose();
        });
    }

    [Test]
    public async Task OperatingTableOpensItsOwnWindow()
    {
        var map = await Pair.CreateTestMap();
        EntityUid table = default;
        EntityUid patient = default;

        await Server.WaitPost(() =>
        {
            table = Server.EntMan.SpawnEntity("OperatingTable", map.GridCoords);
            patient = Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            if (ServerSession is { } session)
                Server.PlayerMan.SetAttachedEntity(session, patient);

            Assert.That(Server.EntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, table), Is.True);

            var scalpel = Server.EntMan.SpawnEntity("Scalpel", map.GridCoords);
            var ev = new InteractUsingEvent(patient, scalpel, table,
                Server.EntMan.GetComponent<TransformComponent>(table).Coordinates);
            Server.EntMan.EventBus.RaiseLocalEvent(table, ev);

            var ui = Server.EntMan.System<Robust.Shared.GameObjects.SharedUserInterfaceSystem>();
            Assert.That(ui.IsUiOpen(table, OperatingComputerUiKey.Key, patient), Is.True,
                "the operating table did not open its own window.");
        });

        await RunTicksSync(60);
        await Pair.RunUntilSynced();

        var clientTable = Pair.ToClientUid(table);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.EntityExists(clientTable), Is.True);
            Assert.That(Client.EntMan.System<Robust.Shared.GameObjects.SharedUserInterfaceSystem>()
                .TryGetOpenUi<OperatingComputerBui>(clientTable, OperatingComputerUiKey.Key, out _), Is.True,
                "the operating table's window did not reach the client.");
        });
    }

    [Test]
    public async Task OperatingComputerOpens()
    {
        var map = await Pair.CreateTestMap();
        EntityUid computer = default;
        EntityUid table = default;
        EntityUid patient = default;
        await Server.WaitPost(() =>
        {
            table = Server.EntMan.SpawnEntity("OperatingTable", map.GridCoords);
            computer = Server.EntMan.SpawnEntity("WFOperatingComputer",
                map.GridCoords.Offset(new Vector2(1, 0)));
            patient = Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            if (ServerSession is { } session)
                Server.PlayerMan.SetAttachedEntity(session, patient);
            Server.EntMan.GetComponent<OperatingComputerComponent>(computer).RequiresPower = false;
            Assert.That(Server.EntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, table), Is.True);

            var scalpel = Server.EntMan.SpawnEntity("Scalpel", map.GridCoords);
            var ev = new InteractUsingEvent(patient, scalpel, computer,
                Server.EntMan.GetComponent<TransformComponent>(computer).Coordinates);
            Server.EntMan.EventBus.RaiseLocalEvent(computer, ev);

            var ui = Server.EntMan.System<Robust.Shared.GameObjects.SharedUserInterfaceSystem>();
            Assert.That(Server.EntMan.HasComponent<Robust.Shared.GameObjects.UserInterfaceComponent>(computer),
                Is.True, "the computer has no UserInterface component.");
            var comp = Server.EntMan.GetComponent<Robust.Shared.GameObjects.UserInterfaceComponent>(computer);
            var field = typeof(Robust.Shared.GameObjects.UserInterfaceComponent).GetField("Interfaces",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var dict = field?.GetValue(comp) as System.Collections.IDictionary;
            Assert.That(dict, Is.Not.Null);
            Assert.That(dict!.Contains(OperatingComputerUiKey.Key), Is.True,
                "the prototype did not register the operating computer interface key.");
            Assert.That(ui.IsUiOpen(computer, OperatingComputerUiKey.Key, patient), Is.True,
                "the server did not open the interface.");
        });

        await RunTicksSync(60);
        await Pair.RunUntilSynced();

        var clientComputer = Pair.ToClientUid(computer);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.EntityExists(clientComputer), Is.True, "the computer never reached the client.");
            Assert.That(Client.EntMan.System<Robust.Shared.GameObjects.SharedUserInterfaceSystem>()
                .TryGetOpenUi<OperatingComputerBui>(clientComputer, OperatingComputerUiKey.Key, out _), Is.True,
                "the operating computer did not open its window.");
        });
    }
}
