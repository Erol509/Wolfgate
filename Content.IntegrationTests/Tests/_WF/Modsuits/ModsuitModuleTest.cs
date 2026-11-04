using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Containers.ItemSlots;
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

            Assert.That(modSuit.TryInstallModule((suit, comp), module, human), "a compact storage module should install");
            Assert.That(modSuit.TryInstallModule((suit, comp), flashlight, human), "a flashlight module should install");
            Assert.That(modSuit.TryInstallModule((suit, comp), jetpack, human), "a jetpack module should install");
            Assert.That(entMan.GetComponent<ModModuleComponent>(flashlight).ActionEntity, Is.Not.Null, "the flashlight action should exist");
            Assert.That(comp.ModulesContainer.ContainedEntities, Does.Contain(module));
            Assert.That(modSuit.GetComplexity((suit, comp)), Is.EqualTo(5));
            Assert.That(entMan.GetComponent<ModModuleComponent>(module).Active, Is.True, "the compact storage module should be active on install");
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
            Assert.That(modSuit.TryUseModule(flashlight, human), Is.False, "a powered module must not run on an empty core");
            foreach (var part in comp.PartEntities.Values)
            {
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed,
                    $"every deployed part should be sealed; part {entMan.ToPrettyString(part)} failed");
            }
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
