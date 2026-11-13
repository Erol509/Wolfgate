using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks a MODsuit deploys, seals, takes modules through the native module system, and stays within its slowdown cap.</summary>
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
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
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
            Assert.That(comp.ModulesContainer.ContainedEntities, Has.Count.EqualTo(2), "the power-cell suit should ship with its two basic modules");

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            Assert.That(modSuit.TryInstallModule((suit, comp), module, human), Is.False,
                "a second storage module should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), flashlight, human), Is.False,
                "a second flashlight should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), jetpack, human), "a jetpack module should install");

            Assert.That(modSuit.GetComplexity((suit, comp)), Is.EqualTo(5));
            Assert.That(entMan.HasComponent<StorageComponent>(suit), Is.True,
                "the compact storage module should grant storage as soon as it is installed");
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

            var suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", new MapCoordinates(0, 0, mapId));
            var comp = entMan.GetComponent<ModSuitComponent>(suit);

            var battery = modSuit.GetBattery((suit, comp));
            Assert.That(battery, Is.Not.Null, "the standard core should ship with a power cell");
            Assert.That(modSuit.GetCharge(battery!.Value), Is.GreaterThan(0f), "the shipped cell should be charged");
            Assert.That(comp.ModulesContainer.ContainedEntities, Has.Count.EqualTo(2),
                "the power-cell suit should ship with a flashlight and a compact storage module");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreAndCellAreItemSlots()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var wires = entMan.System<SharedWiresSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();

        EntityUid human = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            var core = modSuit.GetCore((suit, comp));
            Assert.That(core, Is.Not.Null, "the standard suit should ship with a core");

            Assert.That(itemSlots.TryEject(suit, comp.CoreSlotId, human, out _), Is.False,
                "the sealed maintenance panel should block ejecting the core");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null);

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));
            Assert.That(itemSlots.TryEject(suit, comp.CoreSlotId, human, out var ejected), Is.True,
                "the core should eject with the panel open");
            Assert.That(ejected, Is.EqualTo(core));
            Assert.That(modSuit.GetCore((suit, comp)), Is.Null);

            Assert.That(itemSlots.TryEject(core.Value, "cell_slot", human, out var cell), Is.True,
                "the cell should come out of a loose core");
            Assert.That(cell, Is.Not.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingStorageModuleDropsContentsAndRemovesStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var storage = entMan.System<SharedStorageSystem>();
        var wires = entMan.System<SharedWiresSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        EntityUid module = default;
        var items = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);

            var suitComp = entMan.GetComponent<ModSuitComponent>(suit);
            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            module = suitComp.ModulesContainer.ContainedEntities.First(m =>
                entMan.GetComponent<MetaDataComponent>(m).EntityPrototype?.ID == "ModsuitModuleStorageCompact");
            Assert.That(entMan.TryGetComponent<StorageComponent>(suit, out var suitStorage), Is.True);

            foreach (var proto in new[] { "Wrench", "Screwdriver" })
            {
                var item = entMan.SpawnEntity(proto, coords);
                items.Add(item);
                Assert.That(storage.Insert(suit, item, out _, user: human, storageComp: suitStorage, playSound: false),
                    Is.True, $"{proto} should fit in the suit storage");
            }

            Assert.That(modSuit.TryRemoveModule((suit, suitComp), module, human), Is.True,
                "the storage module should be removable with the panel open");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<StorageComponent>(suit), Is.False,
                "removing the storage module should remove the suit's storage");

            foreach (var item in items)
            {
                Assert.That(entMan.EntityExists(item), Is.True, "the stored item should still exist");
                Assert.That(entMan.GetComponent<TransformComponent>(item).ParentUid,
                    Is.Not.EqualTo(suit), "the stored item should be dropped out of the suit");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryModSuitNamesItsParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var prototypes = client.ResolveDependency<IPrototypeManager>();
            var factory = client.ResolveDependency<IComponentFactory>();

            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                    continue;

                Assert.That(suit.Parts, Has.Count.EqualTo(4),
                    $"{proto.ID} should name four suit parts");

                foreach (var (slot, partId) in suit.Parts)
                {
                    Assert.That(prototypes.TryIndex<EntityPrototype>(partId, out var part), Is.True,
                        $"{proto.ID} names missing part {partId} for {slot}");
                    Assert.That(part!.TryComp<ModSuitSealedVisualsComponent>(out var visuals, factory), Is.True,
                        $"{partId} has no ModSuitSealedVisuals component for its deployed state");
                    Assert.That(visuals!.ClothingVisuals.ContainsKey(slot), Is.True,
                        $"{partId} has no sealed visual for the {slot} slot");
                }
            }
        });

        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var modSuit = entMan.System<ModSuitSystem>();
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var id in new[]
                     {
                         "ClothingModsuitUSSPVaryagPowerCell",
                         "ClothingModsuitVoltaPowerCell",
                         "ADTClothingModsuitBackClownPowerCell",
                     })
            {
                var suit = entMan.SpawnEntity(id, coords);
                var comp = entMan.GetComponent<ModSuitComponent>(suit);

                Assert.That(comp.Parts, Has.Count.EqualTo(4), $"{id} should name four suit parts");
                Assert.That(comp.PartEntities, Has.Count.EqualTo(4), $"{id} should spawn its four suit parts");

                var core = modSuit.GetCore((suit, comp));
                Assert.That(core, Is.Not.Null, $"{id} should ship with a core");
                Assert.That(entMan.HasComponent<ModCoreComponent>(core!.Value), Is.True,
                    $"{id} should use a MOD core");
                Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, $"{id} should ship with a charged cell");
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

    /// <summary>Every deployed suit stays at 10% slowdown at most, or 5% when its piercing protection is low.</summary>
    [Test]
    public async Task EveryModSuitStaysUnderItsSlowdownCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                    continue;

                var parts = suit.Parts;
                if (!parts.ContainsKey("outerClothing")
                    || !prototypes.TryIndex(parts["outerClothing"], out var chest))
                    continue;

                var piercing = 1f;
                if (chest.TryComp<ArmorComponent>(out var armor, factory)
                    && armor.Modifiers.Coefficients.TryGetValue("Piercing", out var value))
                    piercing = value;

                var cap = piercing <= 0.6f ? 0.10f : 0.05f;
                var walk = 1f;
                var sprint = 1f;

                foreach (var partId in parts.Values)
                {
                    if (!prototypes.TryIndex(partId, out var part)
                        || !part.TryComp<ClothingSpeedModifierComponent>(out var speed, factory))
                        continue;

                    walk *= speed.WalkModifier;
                    sprint *= speed.SprintModifier;
                }

                if (proto.TryComp<ClothingSpeedModifierComponent>(out var suitSpeed, factory))
                {
                    walk *= suitSpeed.WalkModifier;
                    sprint *= suitSpeed.SprintModifier;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(1f - walk, Is.LessThanOrEqualTo(cap + 0.0001f),
                        $"{proto.ID} has {1f - walk:P2} walk slowdown, cap is {cap:P0}");
                    Assert.That(1f - sprint, Is.LessThanOrEqualTo(cap + 0.0001f),
                        $"{proto.ID} has {1f - sprint:P2} sprint slowdown, cap is {cap:P0}");
                });
            }
        });

        await pair.CleanReturnAsync();
    }
}
