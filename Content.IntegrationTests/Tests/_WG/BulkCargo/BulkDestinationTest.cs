#nullable enable
using System.Numerics;
using Content.Server._WG.BulkCargo;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// A ship's delivery reach is measured from its hull, so a long hauler docked or holding station by the counter still
/// counts even when its grid origin is far away.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoBulkOrderSystem))]
public sealed class BulkDestinationTest
{
    [Test]
    public async Task LongHullNearTheCounterCounts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var mapSystem = em.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            market.SetVendorStock(grid, BulkCargoTestWorld.Fabric, (int) market.Profile(grid).StockTarget);

            // The hull reaches back towards the counter while the grid origin sits 150 tiles out.
            var ship = BulkCargoTestWorld.Grid(em, map, new Vector2(150, 0), 4, 4);
            em.EnsureComponent<ShuttleComponent>(ship);
            var shipGrid = em.GetComponent<MapGridComponent>(ship);
            for (var x = -45; x < 0; x++)
            {
                for (var y = 0; y < 2; y++)
                    mapSystem.SetTile(ship, shipGrid, new Vector2i(x, y), map.Tile.Tile);
            }

            var first = BulkCargoTestWorld.Marker(em, ship, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, ship, 2, 2);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            Assert.That(zones.TryZone(zone, out var area), Is.True);

            Assert.That(orders.Destinations(vendor).Contains(area.Grid), Is.True,
                "The hull is within reach even though the grid origin is not.");

            var cart = new[] { new WGTradeBulkLine(BulkCargoTestWorld.Fabric, 1) };
            Assert.That(orders.Quote(vendor, buyer, cart, zone), Is.Not.Null,
                "A long hauler whose hull is near the counter should be a valid destination.");
        });

        await pair.CleanReturnAsync();
    }
}
