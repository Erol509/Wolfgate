using System.Linq;
using System.Numerics;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks the hardlight shader and per-theme hardlight data load on the client.</summary>
[TestFixture]
public sealed class ModSuitHardlightTest
{
    [Test]
    public async Task ThemesAndShaderLoad()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            var cache = pair.Client.ResolveDependency<IResourceCache>();
            Assert.That(prototypes.TryIndex<ShaderPrototype>("ModsuitHardlight", out var shaderProto), Is.True,
                "the MODsuit hardlight shader should be registered");

            using (var shader = shaderProto!.InstanceUnique())
            {
                shader.SetParameter("color", new Vector3(0.3f, 0.8f, 1f));
                shader.SetParameter("color2", new Vector3(0.1f, 0.3f, 0.5f));
                shader.SetParameter("alpha", 0.7f);
                shader.SetParameter("intensity", 1.1f);
                shader.SetParameter("maskScale", 384f);

                var theme = prototypes.EnumeratePrototypes<ModThemePrototype>().First(t => t.Hardlight);
                shader.SetParameter("maskTex", cache.GetResource<TextureResource>(theme.HardlightMask).Texture);
            }

            var hardlightThemes = 0;
            foreach (var theme in prototypes.EnumeratePrototypes<ModThemePrototype>())
            {
                if (!theme.Hardlight)
                    continue;

                hardlightThemes++;
                Assert.That(theme.HardlightColor.A, Is.GreaterThan(0f), $"{theme.ID} has a transparent hardlight colour");
                Assert.That(theme.HardlightColor2.A, Is.GreaterThan(0f), $"{theme.ID} has a transparent second hardlight colour");
                Assert.That(cache.TryGetResource<TextureResource>(theme.HardlightMask, out _), Is.True,
                    $"{theme.ID} has no hardlight mask texture");
            }

            Assert.That(hardlightThemes, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ActiveSuitAddsHardlightMaskLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();

        EntityUid mob = default;
        EntityUid suit = default;
        NetEntity mobNet = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            mob = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandard", coords);
            Assert.That(inventory.TryEquip(mob, suit, "back", silent: true, force: true));
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            modSuit.ToggleAllParts((suit, comp), mob);
            mobNet = entMan.GetNetEntity(mob);
        });

        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryToggleActivation((suit, comp), mob));
        });
        await server.WaitRunTicks(360);

        await pair.Client.WaitAssertion(() =>
        {
            var clientEntMan = pair.Client.EntMan;
            var clientMob = clientEntMan.GetEntity(mobNet);
            var sprite = clientEntMan.GetComponent<SpriteComponent>(clientMob);
            var spriteSystem = clientEntMan.System<SpriteSystem>();
            Assert.That(spriteSystem.LayerMapTryGet((clientMob, sprite), "wf-hardlight-mask", out _, false), Is.True,
                "the active suit should add the hardlight mask layer");
        });

        await pair.CleanReturnAsync();
    }
}
