#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.BorgSkins;
using Content.Shared._WF.BorgSkins;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.IntegrationTests.Tests._WF.BorgSkins;

/// <summary>Every Wolfgate borg skin has the states its borg type looks up, and oversized skins are placed and flagged.</summary>
[TestFixture]
[TestOf(typeof(WFBorgSkinSystem))]
public sealed class WFBorgSkinTest
{
    private const string Prefix = "WFBorgSkin";

    [Test]
    public async Task SkinsHaveTheirTypesStates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var count = 0;
            foreach (var subtype in prototypes.EnumeratePrototypes<BorgSubtypePrototype>())
            {
                if (!subtype.ID.StartsWith(Prefix))
                    continue;

                count++;
                var type = prototypes.Index(subtype.ParentBorgType);
                var path = SpriteSpecifierSerializer.TextureRoot / subtype.SpritePath;
                Assert.That(resources.TryGetResource<RSIResource>(path, out var resource), $"{subtype.ID}: no RSI at {path}");
                var rsi = resource!.RSI;

                var states = new List<string>
                {
                    type.SpriteBodyState,
                    type.SpriteHasMindState,
                    type.SpriteNoMindState,
                    type.SpriteToggleLightState,
                };
                if (type.SpriteBodyMovementState is { } movement)
                    states.Add(movement);

                if (prototypes.TryIndex<WFBorgSkinPrototype>(subtype.ID, out var skin) && skin.Walks)
                {
                    foreach (var state in states.ToArray())
                    {
                        if (!state.EndsWith("_moving"))
                            states.Add(state + "_moving");
                    }
                }

                foreach (var state in states)
                {
                    Assert.That(rsi.TryGetState(state, out _), $"{subtype.ID}: {path} has no state {state}");
                }

                foreach (var pose in skin?.Poses ?? new List<WFBorgPose>())
                {
                    var state = pose.ToString().ToLowerInvariant();
                    Assert.That(rsi.TryGetState(state, out _), $"{subtype.ID}: {path} has no pose state {state}");
                }

                Assert.That(prototypes.HasIndex<EntityPrototype>(subtype.DummyPrototype), $"{subtype.ID}: no dummy chassis");
            }

            Assert.That(count, Is.GreaterThan(0));

            foreach (var skin in prototypes.EnumeratePrototypes<WFBorgSkinPrototype>())
            {
                Assert.That(prototypes.HasIndex<BorgSubtypePrototype>(skin.ID), $"wfBorgSkin {skin.ID} matches no borgSubtype");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TallSkinStandsOnItsTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.EntMan;
        var sprites = client.System<SpriteSystem>();

        await client.WaitAssertion(() =>
        {
            var tall = entities.SpawnEntity("WFBorgChassisEngineeringMeka", MapCoordinates.Nullspace);
            var sprite = entities.GetComponent<SpriteComponent>(tall);
            Assert.That(sprites.TryGetLayer((tall, sprite), BorgVisualLayers.Body, out var body, false));
            Assert.Multiple(() =>
            {
                Assert.That(body!.Offset, Is.EqualTo(new Vector2(0f, 0.5f)));
                Assert.That(body.ActualRsi?.Size.Y, Is.EqualTo(64));
                Assert.That(body.State.Name, Is.EqualTo("engineer"));
            });

            var wide = entities.SpawnEntity("WFBorgChassisEngineeringDrake", MapCoordinates.Nullspace);
            sprite = entities.GetComponent<SpriteComponent>(wide);
            Assert.That(sprites.TryGetLayer((wide, sprite), BorgVisualLayers.Body, out body, false));
            Assert.Multiple(() =>
            {
                Assert.That(body!.Offset, Is.EqualTo(Vector2.Zero));
                Assert.That(body.ActualRsi?.Size.X, Is.EqualTo(64));
                Assert.That(entities.TryGetComponent(wide, out WFBorgSkinAnimatedComponent? animated) && animated.Walks);
            });

            entities.DeleteEntity(tall);
            entities.DeleteEntity(wide);
        });

        await pair.CleanReturnAsync();
    }
}
