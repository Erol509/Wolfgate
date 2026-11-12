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
    public async Task EveryRadialIconResolves()
    {
        await Client.WaitAssertion(() =>
        {
            var prototypes = Client.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            var sprites = Client.EntMan.System<Robust.Client.GameObjects.SpriteSystem>();
            foreach (var operation in prototypes.EnumeratePrototypes<SurgicalOperationPrototype>())
            {
                if (operation.Icon != null)
                    Assert.DoesNotThrow(() => sprites.Frame0(operation.Icon), $"{operation.ID} icon does not resolve.");
            }
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
            Assert.That(Server.EntMan.System<SharedBuckleSystem>().TryBuckle(patient, null, table), Is.True);

            var ev = new ActivateInWorldEvent(patient, computer, true);
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
                .TryGetOpenUi<OperatingComputerBui>(clientComputer, OperatingComputerUiKey.Key, out var bui), Is.True,
                "the operating computer did not open its window.");
            Assert.That(bui!.CurrentState, Is.Not.Null);
            Assert.That(bui.CurrentState!.NextOperations, Is.Not.Empty,
                "the computer sent no possible operations for the patient.");
        });
    }
}
