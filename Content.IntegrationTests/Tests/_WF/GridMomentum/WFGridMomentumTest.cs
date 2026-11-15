#nullable enable
using System.Numerics;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.GridMomentum;

/// <summary>A mob that floats off a moving grid keeps the grid's momentum.</summary>
[TestFixture]
[TestOf(typeof(SharedMoverController))]
public sealed class WFGridMomentumTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestMomentumBlock
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        mask:
        - FullTileMask
";

    [Test]
    public async Task MobBesideMovingGridKeepsItsMomentum()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var shipVelocity = new Vector2(6f, 0f);

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            var body = entMan.GetComponent<PhysicsComponent>(grid);
            physics.SetBodyType(grid, BodyType.Dynamic, body: body);
            physics.SetLinearDamping(grid, body, 0f);
            physics.SetAngularDamping(grid, body, 0f);
            // A wall on the deck, so the mob counts as touching something it can push off.
            entMan.SpawnEntity("WFTestMomentumBlock", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var origin = xforms.GetWorldPosition(grid);
            mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(origin + new Vector2(1.55f, 0.5f), map.MapId));
            physics.SetLinearVelocity(grid, shipVelocity, body: body);
            physics.SetLinearVelocity(mob, shipVelocity);
        });

        // Hold the ship's speed against any passive braking, as thrust would.
        for (var i = 0; i < 60; i++)
        {
            await server.WaitPost(() => physics.SetLinearVelocity(grid, shipVelocity));
            await server.WaitRunTicks(1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<TransformComponent>(mob).GridUid, Is.Null, "The mob must float off the grid.");
            var mobVelocity = entMan.GetComponent<PhysicsComponent>(mob).LinearVelocity;
            Assert.That((mobVelocity - shipVelocity).Length(), Is.LessThan(0.1f),
                "A mob beside a moving grid must keep drifting with it, not brake against the map.");
        });

        await pair.CleanReturnAsync();
    }
}
