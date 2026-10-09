using System.Collections.Generic;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.FtlEffects;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.FtlEffects;

/// <summary>Checks actual FTL departure, cancellation and cleanup on a reusable server.</summary>
public sealed class FtlDepartureTest
{
    [TestCase(false, 2f)]
    [TestCase(true, 2f)]
    [TestCase(false, 0f)]
    public async Task DepartureTracksJumpAndCleansUp(bool cancel, float startup)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var source = await pair.CreateTestMap();
        var target = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var shuttles = entities.System<ShuttleSystem>();
        var shuttle = source.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            maps.SetTiles(shuttle, source.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), new Tile(1)),
                (new Vector2i(1, 0), new Tile(1)),
                (new Vector2i(0, 1), new Tile(1)),
                (new Vector2i(1, 1), new Tile(1)),
            });
            Assert.That(shuttles.TryAddFTLDestination(target.MapId, true, out _), Is.True);
            shuttles.FTLToCoordinates(shuttle, entities.GetComponent<ShuttleComponent>(shuttle),
                new EntityCoordinates(target.MapUid, new Vector2(50, 50)), Angle.Zero,
                startupTime: startup, hyperspaceTime: shuttles.DefaultArrivalTime + 2f);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var ftl = entities.GetComponent<FTLComponent>(shuttle);
            var effect = entities.GetComponent<FtlDepartureComponent>(shuttle);
            if (startup > 0f)
            {
                Assert.That(ftl.State, Is.EqualTo(FTLState.Starting));
                Assert.That(effect.Started, Is.EqualTo(ftl.StateTime.Start));
                Assert.That(effect.Departure, Is.EqualTo(ftl.StateTime.End));
                Assert.That(effect.Entered, Is.False);
            }
            else
                Assert.That(effect.Entered, Is.True, "Instant jumps still produce a departure flash.");
            if (cancel)
                entities.RemoveComponent<FTLComponent>(shuttle);
        });

        if (!cancel)
        {
            var entered = false;
            for (var i = 0; i < 180 && !entered; i++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() => entered = entities.GetComponent<FtlDepartureComponent>(shuttle).Entered);
            }
            await server.WaitAssertion(() =>
            {
                Assert.That(entered, Is.True);
                var map = entities.GetComponent<TransformComponent>(shuttle).MapUid;
                Assert.That(entities.HasComponent<FTLMapComponent>(map), Is.True);
                Assert.That(entities.GetComponent<FTLComponent>(shuttle).State, Is.EqualTo(FTLState.Travelling));
            });
        }

        await pair.RunTicksSync(60);
        await server.WaitAssertion(() => Assert.That(entities.HasComponent<FtlDepartureComponent>(shuttle), Is.False));

        if (!cancel)
        {
            var arrived = false;
            for (var i = 0; i < 1200 && !arrived; i++)
            {
                await pair.RunTicksSync(1);
                await server.WaitPost(() => arrived = entities.TryGetComponent<FtlDepartureComponent>(shuttle, out var effect)
                    && effect.Arriving);
            }
            await server.WaitAssertion(() =>
            {
                Assert.That(arrived, Is.True, "A real completed jump must trigger the arrival rush.");
                Assert.That(entities.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(target.MapUid));
                Assert.That(entities.GetComponent<FtlDepartureComponent>(shuttle).Entered, Is.True);
            });
            await pair.RunTicksSync(60);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<FtlDepartureComponent>(shuttle), Is.False);
            entities.DeleteEntity(shuttle);
            var cleanup = new List<EntityUid> { source.MapUid, target.MapUid };
            var query = entities.AllEntityQueryEnumerator<FTLMapComponent>();
            while (query.MoveNext(out var uid, out _))
                cleanup.Add(uid);
            foreach (var uid in cleanup)
                entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
}
