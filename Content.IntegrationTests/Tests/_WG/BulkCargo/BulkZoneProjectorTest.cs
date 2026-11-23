#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WG.BulkCargo;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// The zone projector places two markers at range into one zone, is spent on the second, shatters when dropped with its
/// waiting corner, refuses corners on another grid, and a marker removed by hand drops the zone from the list.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoZoneProjectorSystem))]
public sealed class BulkZoneProjectorTest
{
    private const string Projector = "WGCargoZoneProjector";
    private const string Corner = "WGCargoZoneCorner";

    private static AfterInteractEvent Click(EntityUid user, EntityUid projector, EntityCoordinates at)
    {
        return new AfterInteractEvent(user, projector, null, at, true);
    }

    private static int Corners(IEntityManager em)
    {
        return em.EntityQuery<WGCargoZoneCornerComponent>().Count();
    }

    private static EntityUid PendingCorner(IEntityManager em)
    {
        return em.EntityQuery<WGCargoZoneCornerComponent>().First().Owner;
    }

    [Test]
    public async Task TwoClicksOpenAZone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 8, 8);
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5.5f, 5.5f));
            var projector = em.SpawnEntity(Projector, new EntityCoordinates(grid, 5.5f, 5.5f));

            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 0.5f, 0.5f)));
            Assert.That(Corners(em), Is.EqualTo(1));
            Assert.That(zones.ZonesOn(grid), Is.Empty, "One marker is not a zone yet.");
            Assert.That(em.IsQueuedForDeletion(projector), Is.False);

            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 3.5f, 2.5f)));
            var found = zones.ZonesOn(grid);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Min, Is.EqualTo(new Vector2i(0, 0)));
            Assert.That(found[0].Max, Is.EqualTo(new Vector2i(3, 2)));
            Assert.That(em.IsQueuedForDeletion(projector), Is.True, "One projector opens one zone.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DroppingDestroysProjectorAndWaitingCorner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 8, 8);
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5.5f, 5.5f));
            var projector = em.SpawnEntity(Projector, new EntityCoordinates(grid, 5.5f, 5.5f));

            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 0.5f, 0.5f)));
            var corner = PendingCorner(em);

            em.EventBus.RaiseLocalEvent(projector, new DroppedEvent(user));

            Assert.That(em.IsQueuedForDeletion(projector), Is.True, "A dropped projector is destroyed.");
            Assert.That(em.IsQueuedForDeletion(corner), Is.True, "Its waiting corner goes with it.");
            Assert.That(zones.ZonesOn(grid), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CornersOnAnotherGridDoNotPair()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var xforms = em.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 8, 8);
            var far = BulkCargoTestWorld.Grid(em, map, new Vector2(40, 40), 4, 4);
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5.5f, 5.5f));
            var projector = em.SpawnEntity(Projector, new EntityCoordinates(grid, 5.5f, 5.5f));

            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 0.5f, 0.5f)));

            // Walk to the other grid and try to close the zone there.
            xforms.SetCoordinates(user, new EntityCoordinates(far, 1.5f, 1.5f));
            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(far, 2.5f, 2.5f)));

            Assert.That(zones.ZonesOn(grid), Is.Empty);
            Assert.That(zones.ZonesOn(far), Is.Empty);
            Assert.That(em.IsQueuedForDeletion(projector), Is.False, "The projector is still waiting for a valid pair.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingACornerDropsTheZone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 8, 8);
            var user = em.SpawnEntity("MobHuman", new EntityCoordinates(grid, 5.5f, 5.5f));
            var projector = em.SpawnEntity(Projector, new EntityCoordinates(grid, 5.5f, 5.5f));

            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 0.5f, 0.5f)));
            em.EventBus.RaiseLocalEvent(projector, Click(user, projector, new EntityCoordinates(grid, 3.5f, 2.5f)));
            Assert.That(zones.ZonesOn(grid), Has.Count.EqualTo(1));

            var corner = em.EntityQuery<WGCargoZoneCornerComponent>().First().Owner;
            em.EventBus.RaiseLocalEvent(corner, new InteractHandEvent(user, corner));

            Assert.That(zones.ZonesOn(grid), Is.Empty, "Losing a marker closes the zone.");
            Assert.That(em.IsQueuedForDeletion(corner), Is.True);

            var other = em.EntityQuery<WGCargoZoneCornerComponent>().First(comp => comp.Owner != corner);
            Assert.That(other.Zone, Is.EqualTo(Guid.Empty), "The other marker is left unpaired.");
        });

        await pair.CleanReturnAsync();
    }
}
