#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Power.Components;
using Content.Server._WG.BulkCargo;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.BulkCargo;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// The delivery queue sends one crate per step into its reserved tile, re-checks the tile where it stands, replaces a
/// blocked one while the zone can spare it, pauses without power, and refunds the tail when it can't go on.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoBulkOrderSystem))]
public sealed class BulkDeliveryQueueTest
{
    private const string Good = BulkCargoTestWorld.Fabric;

    /// <summary>Puts a vendor on a floored station grid, powered, with a bulk component of its own.</summary>
    private static (EntityUid Vendor, Guid Zone) World(
        IEntityManager em,
        TestMapData map,
        WGCargoDeliveryZoneSystem zones,
        out EntityUid grid)
    {
        grid = map.Grid.Owner;
        BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
        var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));

        // A test map has no APC: declare the machine internally powered, and use the switch to cut it.
        var power = em.EnsureComponent<ApcPowerReceiverComponent>(vendor);
        power.NeedsPower = false;
        power.PowerDisabled = false;
        power.Powered = true;
        power.Recalculate = true;

        em.EnsureComponent<WGCargoBulkVendorComponent>(vendor);
        var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
        var second = BulkCargoTestWorld.Marker(em, grid, 3, 2);
        Assert.That(zones.TryPair(first, second, out var zone), Is.True);
        return (vendor, zone);
    }

    private static List<Vector2i> CrateCells(IEntityManager em, EntityUid grid)
    {
        var map = em.System<SharedMapSystem>();
        var gridComp = em.GetComponent<MapGridComponent>(grid);
        var cells = new List<Vector2i>();
        var query = em.EntityQueryEnumerator<WGBulkPurchasedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                cells.Add(map.TileIndicesFor(grid, gridComp, xform.Coordinates));
        }

        return cells;
    }

    [Test]
    public async Task DeliversOneCrateAtATime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var cart = new[] { new WGTradeBulkLine(Good, 3) };
            var priced = market.CartQuote(vendor, cart)!;
            bank.TryGetBalance(buyer, out var before);
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            // One step, one crate, with no tick in between to deliver the rest.
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            Assert.That(order.Delivered, Is.EqualTo(1), "One crate per step.");
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(1));

            var firstCell = CrateCells(em, grid)[0];
            var screen = orders.BuildState(vendor).Zones.First(entry => entry.Id == zone);
            Assert.That(screen.Landed, Does.Contain(firstCell),
                "The preview should find the delivered crate in the zone through the grid lookup.");

            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.TryGetOrder(orderId, out _), Is.False, "A finished order leaves the queue.");

            var cells = CrateCells(em, grid);
            Assert.That(cells, Has.Count.EqualTo(3));
            Assert.That(cells.Distinct().Count(), Is.EqualTo(3), "One crate per tile.");
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start - 3),
                "Delivered crates must not take stock a second time.");
            bank.TryGetBalance(buyer, out var after);
            Assert.That(after, Is.EqualTo(before - priced.Total), "The order costs exactly the quoted total.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelPartwayRefundsOnlyTheRest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var cart = new[] { new WGTradeBulkLine(Good, 3) };
            var priced = market.CartQuote(vendor, cart)!;
            var firstPrice = priced.Lines[0].Prices[0];
            bank.TryGetBalance(buyer, out var before);
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.TryCancel(buyer, orderId), Is.True);

            bank.TryGetBalance(buyer, out var after);
            Assert.That(after, Is.EqualTo(before - firstPrice),
                "Only the crate that arrived stays paid for; the rest is refunded.");
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(1), "The delivered crate stays.");
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start - 1),
                "Only the crate that arrived stays off the shelf.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BlockedCellsRefundTheOrder()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var power = em.EnsureComponent<ApcPowerReceiverComponent>(vendor);
            power.NeedsPower = false;
            power.PowerDisabled = false;
            power.Powered = true;

            // A vendor that gives up on a blocked tile at once, with exactly two tiles to fill.
            var settings = em.EnsureComponent<WGCargoBulkVendorComponent>(vendor);
            settings.BlockedTimeout = TimeSpan.Zero;

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 0, 1);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var cart = new[] { new WGTradeBulkLine(Good, 2) };
            var priced = market.CartQuote(vendor, cart)!;
            bank.TryGetBalance(buyer, out var before);
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            // Wall off both reserved tiles before the first crate can land.
            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            foreach (var tile in order.Cells)
            {
                var wall = em.SpawnEntity("WallSolid", new EntityCoordinates(grid, tile.X + 0.5f, tile.Y + 0.5f));
                var xform = em.GetComponent<TransformComponent>(wall);
                if (!xform.Anchored)
                    em.System<SharedTransformSystem>().AnchorEntity(wall, xform);
            }

            Assert.That(orders.DeliverNext(orderId), Is.False, "An order that can't land is stopped.");
            Assert.That(CrateCells(em, grid), Is.Empty);
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start), "Its crates return to the shelf.");
            bank.TryGetBalance(buyer, out var after);
            Assert.That(after, Is.EqualTo(before), "Its whole cost is refunded.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReplacesABlockedCell()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var power = em.EnsureComponent<ApcPowerReceiverComponent>(vendor);
            power.NeedsPower = false;
            power.PowerDisabled = false;
            power.Powered = true;
            em.EnsureComponent<WGCargoBulkVendorComponent>(vendor);

            // Four tiles in a row for three crates, so a blocked one can be replaced.
            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 0);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var cart = new[] { new WGTradeBulkLine(Good, 3) };
            var priced = market.CartQuote(vendor, cart)!;
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            // Block the first reserved tile after acceptance.
            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            var blocked = order.Cells[0];
            var wall = em.SpawnEntity("WallSolid", new EntityCoordinates(grid, blocked.X + 0.5f, blocked.Y + 0.5f));
            var xform = em.GetComponent<TransformComponent>(wall);
            if (!xform.Anchored)
                em.System<SharedTransformSystem>().AnchorEntity(wall, xform);

            Assert.That(orders.DeliverNext(orderId), Is.True, "The crate should take the spare tile.");
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.TryGetOrder(orderId, out _), Is.False);
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(3));
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start - 3));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PowerLossPausesDelivery()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            market.SetVendorStock(grid, Good, (int) market.Profile(grid).StockTarget);

            var cart = new[] { new WGTradeBulkLine(Good, 2) };
            var priced = market.CartQuote(vendor, cart)!;
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;
            var power = em.GetComponent<ApcPowerReceiverComponent>(vendor);
            power.Powered = false;

            Assert.That(orders.DeliverNext(orderId), Is.False, "No crates while the vendor has no power.");
            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            Assert.That(order.Delivered, Is.Zero);

            power.Powered = true;
            Assert.That(orders.DeliverNext(orderId), Is.True, "Delivery resumes with power.");
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingTheZoneStopsAndRefundsTheRest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var cart = new[] { new WGTradeBulkLine(Good, 3) };
            var priced = market.CartQuote(vendor, cart)!;
            var firstPrice = priced.Lines[0].Prices[0];
            bank.TryGetBalance(buyer, out var before);
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            Assert.That(orders.DeliverNext(orderId), Is.True);

            // A player picks one marker up while crates are still waiting.
            var corner = em.EntityQuery<WGCargoZoneCornerComponent>().First().Owner;
            zones.Unpair(corner);

            Assert.That(orders.TryGetOrder(orderId, out _), Is.False, "Removing the zone stops its orders.");
            bank.TryGetBalance(buyer, out var after);
            Assert.That(after, Is.EqualTo(before - firstPrice),
                "Only the crate that arrived stays paid for.");
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(1));
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start - 1),
                "The undelivered crates go back on the shelf.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueuesSeveralCrateTypesInOneZone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            market.SetVendorStock(grid, BulkCargoTestWorld.Ammo, target);

            var cart = new[]
            {
                new WGTradeBulkLine(Good, 3),
                new WGTradeBulkLine(BulkCargoTestWorld.Ammo, 2),
            };
            var priced = market.CartQuote(vendor, cart)!;
            Assert.That(priced.Crates, Is.EqualTo(5));
            var orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            Assert.That(order.Goods.Distinct(), Is.EquivalentTo(new[] { Good, BulkCargoTestWorld.Ammo }),
                "One order should keep every crate type from its cart.");
            Assert.That(order.Cells, Has.Count.EqualTo(5), "Both types share the one zone's reserved cells.");

            for (var i = 0; i < 5; i++)
                Assert.That(orders.DeliverNext(orderId), Is.True);

            Assert.That(orders.TryGetOrder(orderId, out _), Is.False);
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(5));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TwoOrdersShareAZoneWhenTheyFit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);
            market.SetVendorStock(grid, BulkCargoTestWorld.Ammo, target);

            var fabric = new[] { new WGTradeBulkLine(Good, 2) };
            var ammo = new[] { new WGTradeBulkLine(BulkCargoTestWorld.Ammo, 2) };
            var fabricPrice = market.CartQuote(vendor, fabric)!;
            var ammoPrice = market.CartQuote(vendor, ammo)!;
            var fabricOrder = orders.PlaceOrder(vendor, buyer,
                orders.Quote(vendor, buyer, fabric, zone)!.Value, fabricPrice.Total)!.Value;
            var ammoOrder = orders.PlaceOrder(vendor, buyer,
                orders.Quote(vendor, buyer, ammo, zone)!.Value, ammoPrice.Total)!.Value;

            Assert.That(orders.TryGetOrder(fabricOrder, out var first), Is.True);
            Assert.That(orders.TryGetOrder(ammoOrder, out var second), Is.True);
            Assert.That(first.Cells.Intersect(second.Cells), Is.Empty,
                "Two queued orders must not reserve the same crate tile.");

            for (var i = 0; i < 2; i++)
            {
                Assert.That(orders.DeliverNext(fabricOrder), Is.True);
                Assert.That(orders.DeliverNext(ammoOrder), Is.True);
            }

            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(4));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CounterDispensesBesideTheVendorAndWaits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        Guid orderId = default;
        var walls = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, _) = World(em, map, zones, out var grid);
            market.SetVendorStock(grid, Good, (int) market.Profile(grid).StockTarget);

            var cart = new[] { new WGTradeBulkLine(Good, 2) };
            var priced = market.CartQuote(vendor, cart)!;
            orderId = orders.PlaceOrder(vendor, buyer,
                orders.Quote(vendor, buyer, cart, WGCargoBulkDestinations.Counter)!.Value, priced.Total)!.Value;

            Assert.That(orders.TryGetOrder(orderId, out var order), Is.True);
            Assert.That(order.Cells, Is.Empty, "A counter order reserves no zone cells.");
            Assert.That(order.CounterCells, Is.Not.Empty, "The vendor must have floor tiles beside it.");
            Assert.That(orders.DeliverNext(orderId), Is.True);

            var first = CrateCells(em, grid).Single();
            foreach (var tile in order.CounterCells)
            {
                if (tile == first)
                    continue;

                var wall = em.SpawnEntity("WallSolid", new EntityCoordinates(grid, tile.X + 0.5f, tile.Y + 0.5f));
                var xform = em.GetComponent<TransformComponent>(wall);
                if (!xform.Anchored)
                    em.System<SharedTransformSystem>().AnchorEntity(wall, xform);
                walls.Add(wall);
            }

            // Every other counter tile is occupied: the order waits instead of stacking or refunding.
            Assert.That(orders.DeliverNext(orderId), Is.True);
            Assert.That(orders.TryGetOrder(orderId, out order), Is.True);
            Assert.That(order.Delivered, Is.EqualTo(1));
            Assert.That(CrateCells(em, grid), Has.Count.EqualTo(1));
        });

        await server.WaitAssertion(() =>
        {
            em.QueueDeleteEntity(walls[0]);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(orders.DeliverNext(orderId), Is.True, "A freed counter tile lets the next crate arrive.");
            Assert.That(orders.TryGetOrder(orderId, out _), Is.False);
            Assert.That(CrateCells(em, map.Grid.Owner), Has.Count.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QueueRunsOnItsOwn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        Guid orderId = default;
        await server.WaitAssertion(() =>
        {
            var buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            (var vendor, var zone) = World(em, map, zones, out var grid);
            market.SetVendorStock(grid, Good, (int) market.Profile(grid).StockTarget);

            var cart = new[] { new WGTradeBulkLine(Good, 2) };
            var priced = market.CartQuote(vendor, cart)!;
            orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;
        });

        // The update loop, not a test call, should empty the tape well within five seconds.
        await server.WaitRunTicks(300);
        await server.WaitAssertion(() =>
        {
            Assert.That(orders.TryGetOrder(orderId, out _), Is.False, "The queue delivers on its own schedule.");
            Assert.That(CrateCells(em, map.Grid.Owner), Has.Count.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }
}
