#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WG.BulkCargo;
using Content.Server.Decals;
using Content.Shared._WG.BulkCargo;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// Two placed markers open a grid-local rectangle; walls, space and physics fixtures block a tile; and one order's
/// reserved tiles can never be handed to another.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoDeliveryZoneSystem))]
public sealed class BulkDeliveryZoneTest
{
    [Test]
    public async Task TwoMarkersFormARectangle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var decals = em.System<DecalSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 4, 3);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 2);
            Assert.That(zones.TryPair(first, second, out var id), Is.True);
            Assert.That(zones.TryZone(id, out var zone), Is.True);

            Assert.That(zone.Min, Is.EqualTo(new Vector2i(0, 0)));
            Assert.That(zone.Max, Is.EqualTo(new Vector2i(3, 2)));
            Assert.That(zone.Width, Is.EqualTo(4));
            Assert.That(zone.Height, Is.EqualTo(3));
            Assert.That(zone.Cells, Is.EqualTo(12));
            Assert.That(zones.FreeCount(zone), Is.EqualTo(12), "A clear floor should take a crate on every tile.");
            Assert.That(zones.ZonesOn(grid), Has.Count.EqualTo(1));

            // One drawn corner is turned to face into the rectangle: the low marker is south-west, the high one
            // north-east, relative to the marker art's north-west base.
            Assert.That(em.GetComponent<TransformComponent>(first).LocalRotation.Theta,
                Is.EqualTo(Math.PI / 2).Within(0.001));
            Assert.That(em.GetComponent<TransformComponent>(second).LocalRotation.Theta,
                Is.EqualTo(-Math.PI / 2).Within(0.001));

            // The zone is painted on the floor: corners, edge lines and a fill.
            var painted = decals.GetDecalsInRange(grid, new Vector2(0, 0), 20f);
            Assert.That(painted, Is.Not.Empty, "A formed zone should paint its warning decals.");

            var field = em.EntityQuery<WGCargoZoneFieldComponent>().FirstOrDefault();
            Assert.That(field, Is.Not.Null, "A formed zone should carry its energy field.");
            Assert.That(field!.Min, Is.EqualTo(new Vector2i(0, 0)));
            Assert.That(field.Max, Is.EqualTo(new Vector2i(3, 2)));
            var fieldUid = field.Owner;

            // A corner belongs to one zone, and a zone needs both of its corners.
            var third = BulkCargoTestWorld.Marker(em, grid, 1, 1);
            Assert.That(zones.TryPair(first, third, out _), Is.False);
            Assert.That(zones.TryPair(third, third, out _), Is.False);

            zones.Unpair(second);
            Assert.That(zones.TryZone(id, out _), Is.False, "Losing a corner should drop the zone.");
            Assert.That(zones.ZonesOn(grid), Is.Empty);
            Assert.That(em.GetComponent<TransformComponent>(first).LocalRotation.Theta,
                Is.EqualTo(0).Within(0.001), "A freed marker returns to its base orientation.");
            Assert.That(decals.GetDecalsInRange(grid, new Vector2(0, 0), 20f), Is.Empty,
                "Removing the zone should remove its floor markings.");
            Assert.That(em.IsQueuedForDeletion(fieldUid), Is.True, "Removing the zone should remove its energy field.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OversizedAndOverCapZonesAreRefused()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 20, 20);

            // 17 x 17 is 289 tiles, over the 256 cap.
            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 16, 16);
            Assert.That(zones.TryPair(first, second, out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("wg-bulk-zone-too-large"));
            Assert.That(zones.ZonesOn(grid), Is.Empty);

            // A full grid's worth of one-tile zones fits; one more does not.
            for (var i = 0; i < WGCargoDeliveryZoneSystem.MaxZonesPerGrid; i++)
            {
                var a = BulkCargoTestWorld.Marker(em, grid, i, 18);
                var b = BulkCargoTestWorld.Marker(em, grid, i, 19);
                Assert.That(zones.TryPair(a, b, out _), Is.True, $"Zone {i} should open.");
            }

            var extraA = BulkCargoTestWorld.Marker(em, grid, 17, 18);
            var extraB = BulkCargoTestWorld.Marker(em, grid, 17, 19);
            Assert.That(zones.TryPair(extraA, extraB, out _, out error), Is.False);
            Assert.That(error, Is.EqualTo("wg-bulk-zone-too-many"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ZonesCannotOverlap()
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

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 2);
            Assert.That(zones.TryPair(first, second, out _), Is.True);

            // A second zone crossing the first is refused, whether it overlaps or sits wholly inside it.
            var crossA = BulkCargoTestWorld.Marker(em, grid, 2, 1);
            var crossB = BulkCargoTestWorld.Marker(em, grid, 5, 4);
            Assert.That(zones.TryPair(crossA, crossB, out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("wg-bulk-zone-overlap"));

            var insideA = BulkCargoTestWorld.Marker(em, grid, 1, 1);
            var insideB = BulkCargoTestWorld.Marker(em, grid, 2, 2);
            Assert.That(zones.TryPair(insideA, insideB, out _, out error), Is.False);
            Assert.That(error, Is.EqualTo("wg-bulk-zone-overlap"));
            Assert.That(zones.ZonesOn(grid), Has.Count.EqualTo(1));

            // Touching at the edge is allowed.
            var besideA = BulkCargoTestWorld.Marker(em, grid, 4, 0);
            var besideB = BulkCargoTestWorld.Marker(em, grid, 6, 2);
            Assert.That(zones.TryPair(besideA, besideB, out _), Is.True);
            Assert.That(zones.ZonesOn(grid), Has.Count.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlockedSpaceAndOccupiedTilesAreNotFree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 4, 4);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 3);
            Assert.That(zones.TryPair(first, second, out var id), Is.True);
            Assert.That(zones.TryZone(id, out var zone), Is.True);

            // A wall.
            var wall = em.SpawnEntity("WallSolid", new EntityCoordinates(grid, 1.5f, 1.5f));
            var wallXform = em.GetComponent<TransformComponent>(wall);
            if (!wallXform.Anchored)
                em.System<SharedTransformSystem>().AnchorEntity(wall, wallXform);

            // Another crate, which the engine's own crate mask sees.
            em.SpawnEntity(BulkCargoTestWorld.Fabric, new EntityCoordinates(grid, 2.5f, 2.5f));

            // And a hole to space.
            em.System<SharedMapSystem>().SetTile(grid, em.GetComponent<MapGridComponent>(grid), new Vector2i(3, 3), Tile.Empty);

            var count = zones.Count(zone);
            Assert.That(count.Free, Is.EqualTo(13), "Three of sixteen tiles should be unusable.");
            Assert.That(count.Blocked, Is.EqualTo(2), "A wall and a crate block their tiles.");
            Assert.That(count.Space, Is.EqualTo(1), "A space tile can't take a crate.");
            Assert.That(count.Reserved, Is.Zero);

            Assert.That(zones.Classify(zone, new Vector2i(1, 1)), Is.EqualTo(WGCargoCellState.Blocked));
            Assert.That(zones.Classify(zone, new Vector2i(2, 2)), Is.EqualTo(WGCargoCellState.Blocked));
            Assert.That(zones.Classify(zone, new Vector2i(3, 3)), Is.EqualTo(WGCargoCellState.Space));
            Assert.That(zones.Classify(zone, new Vector2i(0, 3)), Is.EqualTo(WGCargoCellState.Free));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReservedCellsAreNotShared()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var zones = em.System<WGCargoDeliveryZoneSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 3, 3);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 2, 2);
            Assert.That(zones.TryPair(first, second, out var id), Is.True);
            Assert.That(zones.TryZone(id, out var zone), Is.True);

            var one = Guid.NewGuid();
            var two = Guid.NewGuid();

            Assert.That(zones.TryReserve(one, zone, 4, out var cells), Is.True);
            Assert.That(cells, Has.Count.EqualTo(4));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(4), "Every crate gets its own tile.");
            Assert.That(zones.FreeCount(zone), Is.EqualTo(5));
            foreach (var tile in cells)
                Assert.That(zones.IsReserved(grid, tile, one), Is.False, "A job may re-check its own cells.");
            foreach (var tile in cells)
                Assert.That(zones.IsReserved(grid, tile), Is.True);

            // A second order can't take more than what's left.
            Assert.That(zones.TryReserve(two, zone, 6, out _), Is.False);
            Assert.That(zones.FreeCount(zone), Is.EqualTo(5), "A failed reservation should take nothing.");
            Assert.That(zones.TryReserve(two, zone, 5, out _), Is.True);
            Assert.That(zones.FreeCount(zone), Is.Zero);

            zones.Release(one);
            Assert.That(zones.FreeCount(zone), Is.EqualTo(4), "Releasing one order returns only its cells.");
            zones.Release(two);
            Assert.That(zones.FreeCount(zone), Is.EqualTo(9));
        });

        await pair.CleanReturnAsync();
    }
}
