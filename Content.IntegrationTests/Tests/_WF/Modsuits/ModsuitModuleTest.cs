using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks a ported MODsuit deploys, seals, and takes a module through the native module system.</summary>
[TestFixture]
public sealed class ModsuitModuleTest
{
    [Test]
    public async Task SuitDeploysSealsAndAcceptsModule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();
        var wires = entMan.System<SharedWiresSystem>();
        var batteries = entMan.System<SharedBatterySystem>();

        EntityUid human = default;
        EntityUid suit = default;
        EntityUid module = default;
        EntityUid flashlight = default;
        EntityUid jetpack = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandartPowerCell", coords);
            module = entMan.SpawnEntity("ModsuitModuleStorageCompact", coords);
            flashlight = entMan.SpawnEntity("ModsuitModuleFlashlight", coords);
            jetpack = entMan.SpawnEntity("ModsuitModuleJetpack", coords);

            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ModSuitComponent>(suit), "the control unit should use the native module system");
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.Parts, Has.Count.EqualTo(4), $"the prototype should name four parts, got {comp.Parts.Count}");
            Assert.That(comp.PartEntities, Has.Count.EqualTo(4), $"the suit should spawn its four parts, got {comp.PartEntities.Count}");
            var battery = modSuit.GetBattery((suit, comp));
            Assert.That(battery, Is.Not.Null, "the standard core should carry a power cell");
            Assert.That(modSuit.GetCharge(battery!.Value), Is.GreaterThan(0f), "the core cell should start charged");
            var core = modSuit.GetCore((suit, comp));
            Assert.That(core, Is.Not.Null);
            Assert.That(entMan.System<ItemSlotsSystem>().TryGetSlot(suit, comp.CoreSlotId, out var coreSlot), Is.True);
            var eject = new ItemSlotEjectAttemptEvent(suit, core!.Value, human, coreSlot);
            entMan.EventBus.RaiseLocalEvent(suit, ref eject);
            Assert.That(eject.Cancelled, Is.True, "the core must not be ejectable like a cell");

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            Assert.That(modSuit.TryInstallModule((suit, comp), module, human), Is.False,
                "a second storage module should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), flashlight, human), Is.False,
                "a second flashlight should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), jetpack, human), "a jetpack module should install");

            EntityUid? installedFlashlight = null;
            EntityUid? installedStorage = null;
            foreach (var installed in comp.ModulesContainer.ContainedEntities)
            {
                var id = entMan.GetComponent<MetaDataComponent>(installed).EntityPrototype?.ID;
                if (id == "ModsuitModuleFlashlight")
                    installedFlashlight = installed;
                else if (id == "ModsuitModuleStorageCompact")
                    installedStorage = installed;
            }

            Assert.That(installedFlashlight, Is.Not.Null, "the suit should start with a flashlight");
            Assert.That(entMan.GetComponent<ModModuleComponent>(installedFlashlight!.Value).ActionEntity, Is.Not.Null,
                "the starting flashlight action should exist");
            Assert.That(comp.ModulesContainer.ContainedEntities, Does.Not.Contain(module),
                "the rejected storage module should not be installed");
            Assert.That(modSuit.GetComplexity((suit, comp)), Is.EqualTo(6));
            Assert.That(installedStorage, Is.Not.Null, "the suit should start with a compact storage module");
            Assert.That(entMan.GetComponent<ModModuleComponent>(installedStorage!.Value).Active, Is.True,
                "the starting compact storage module should be active on install");
            var storage = entMan.GetComponent<StorageComponent>(suit);
            Assert.That(storage.Grid, Has.Count.EqualTo(1), "the compact module should replace the satchel grid");
            Assert.That(storage.Grid[0], Is.EqualTo(new Box2i(0, 0, 5, 3)), "the compact module grid should be applied");
            Assert.That(wires.TogglePanel(suit, panel, false, human), "the panel should close before sealing");

            Assert.That(comp.Wearer, Is.EqualTo(human), "the suit should know its wearer");
            modSuit.ToggleAllParts((suit, comp), human);
            Assert.That(comp.DeployQueue, Is.Not.Empty, "deploying should queue the parts");
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.AllPartsDeployed((suit, comp)), "all four parts should be deployed");
            Assert.That(modSuit.TryToggleActivation((suit, comp), human));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active), "the suit should finish sealing");
            Assert.That(modSuit.TryUseModule(jetpack, human), "the ion jetpack should switch on in weightless space");
            Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), "the jetpack should be active");
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null);
            var coreBattery = modSuit.GetBattery((suit, comp))!.Value;
            batteries.ChangeCharge(coreBattery.Owner, -100000f);

            EntityUid? installedFlashlight = null;
            foreach (var installed in comp.ModulesContainer.ContainedEntities)
            {
                if (entMan.GetComponent<MetaDataComponent>(installed).EntityPrototype?.ID == "ModsuitModuleFlashlight")
                {
                    installedFlashlight = installed;
                    break;
                }
            }

            Assert.That(installedFlashlight, Is.Not.Null);
            Assert.That(modSuit.TryUseModule(installedFlashlight!.Value, human), Is.False,
                "a powered module must not run on an empty core");
            foreach (var part in comp.PartEntities.Values)
            {
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed,
                    $"every deployed part should be sealed; part {entMan.ToPrettyString(part)} failed");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StandardSuitShipsReadyToUse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);

            var suit = entMan.SpawnEntity("ClothingModsuitStandard", new MapCoordinates(0, 0, mapId));
            var comp = entMan.GetComponent<ModSuitComponent>(suit);

            var battery = modSuit.GetBattery((suit, comp));
            Assert.That(battery, Is.Not.Null, "the standard core should ship with a power cell");
            Assert.That(modSuit.GetCharge(battery!.Value), Is.GreaterThan(0f), "the shipped cell should be charged");
            Assert.That(comp.ModulesContainer.ContainedEntities, Has.Count.EqualTo(3),
                "the suit should ship with its three basic modules");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreIsUnboltedWithAWrench()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var wires = entMan.System<SharedWiresSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        EntityUid wrench = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandard", coords);
            wrench = entMan.SpawnEntity("Wrench", coords);

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            var ev = new InteractUsingEvent(human, wrench, suit, entMan.GetComponent<TransformComponent>(suit).Coordinates);
            entMan.EventBus.RaiseLocalEvent(suit, ev, true);

            Assert.That(ev.Handled, Is.True, "a wrench on the open panel should start the unbolting do-after");
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null, "the core should only leave after the wrench do-after");
        });

        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.GetCore((suit, comp)), Is.Null, "the core should be unbolted after the wrench do-after");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PowerCellIsRemovedByTheVerbAfterADoAfter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var modCore = entMan.System<ModCoreSystem>();
        var wires = entMan.System<SharedWiresSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        EntityUid core = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandard", coords);

            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, "the standard suit should ship with a cell");
            core = modSuit.GetCore((suit, comp))!.Value;

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            // Touching the suit must not eject the cell or the core.
            var ev = new InteractHandEvent(human, suit);
            entMan.EventBus.RaiseLocalEvent(suit, ev, true);
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, "a bare hand must not remove the cell");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null, "a bare hand must not remove the core");

            Assert.That(modCore.TryStartCellRemoval(core, suit, human), Is.True,
                "the remove-cell verb should start the do-after");
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, "the cell should not pop out on the first click");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null, "the core itself should not be ejected by the verb");
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, "the cell should still be present early in the do-after");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null, "the core should still be present early in the do-after");
        });

        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.GetBattery((suit, comp)), Is.Null, "the cell should be removed after the do-after");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null, "the core should remain after removing the cell");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryPortedModuleHasAComponent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.ID.StartsWith("ModsuitModule", StringComparison.Ordinal))
                    continue;

                Assert.That(proto.TryComp<ModModuleComponent>(out _, factory), Is.True,
                    $"{proto.ID} is a module prototype but has no ModModule component");
                Assert.DoesNotThrow(() => entMan.SpawnEntity(proto.ID, coords),
                    $"{proto.ID} should initialise");
            }
        });

        await pair.CleanReturnAsync();
    }
}
