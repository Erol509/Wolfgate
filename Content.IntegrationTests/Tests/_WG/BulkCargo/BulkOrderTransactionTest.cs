#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.VendingMachine;
using Content.Server._NF.Bank;
using Content.Server._WG.BulkCargo;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// A bulk order is one transaction: it charges the cart's marginal total, takes every line off the shelf, reserves one
/// tile per crate, and a refusal moves neither money nor stock. Cancelling pays back exactly what did not arrive.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoBulkOrderSystem))]
public sealed class BulkOrderTransactionTest
{
    private const string Good = BulkCargoTestWorld.Fabric;
    private const string Other = BulkCargoTestWorld.Ammo;

    [Test]
    public async Task OrderChargesStockAndReservesCells()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        EntityUid buyer = default!;

        await server.WaitAssertion(() =>
        {
            buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var profile = market.Profile(grid);
            var target = (int) profile.StockTarget;
            market.SetVendorStock(grid, Good, target);
            market.SetVendorStock(grid, Other, target);
            var startGood = market.AvailableStock(grid, Good);
            var startOther = market.AvailableStock(grid, Other);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 5, 3);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            Assert.That(zones.TryZone(zone, out var area), Is.True);
            Assert.That(zones.FreeCount(area), Is.EqualTo(24));

            var cart = new[]
            {
                new WGTradeBulkLine(Good, 10),
                new WGTradeBulkLine(Other, 4),
            };
            var priced = market.CartQuote(vendor, cart)!;
            Assert.That(em.HasComponent<WGTradeVendorComponent>(vendor), Is.True);
            Assert.That(orders.Destinations(vendor).Contains(area.Grid), Is.True,
                $"The zone is not on the vendor's station. vendor grid {em.GetComponent<TransformComponent>(vendor).GridUid}, zone grid {area.Grid}, destinations {string.Join(",", orders.Destinations(vendor))}");
            Assert.That(zones.FreeCount(area), Is.GreaterThanOrEqualTo(14), "The zone has too few free tiles.");
            var quote = orders.Quote(vendor, buyer, cart, zone);
            Assert.That(quote, Is.Not.Null);

            bank.TryGetBalance(buyer, out var before);
            var id = orders.PlaceOrder(vendor, buyer, quote!.Value, priced.Total);
            Assert.That(id, Is.Not.Null, "A funded cart with free cells should be accepted.");
            Assert.That(orders.TryGetOrder(id!.Value, out var order), Is.True);

            Assert.That(order.Goods, Has.Count.EqualTo(14));
            Assert.That(order.Prices, Has.Count.EqualTo(14));
            Assert.That(order.Prices.Sum(), Is.EqualTo(priced.Total), "The tape should hold what the quote charged.");
            Assert.That(order.Cells, Has.Count.EqualTo(14));
            Assert.That(order.Cells.Distinct().Count(), Is.EqualTo(14), "One crate per tile.");
            Assert.That(order.Delivered, Is.Zero);

            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(startGood - 10));
            Assert.That(market.AvailableStock(grid, Other), Is.EqualTo(startOther - 4));
            Assert.That(zones.FreeCount(area), Is.EqualTo(10), "The order's cells are spoken for.");

            bank.TryGetBalance(buyer, out var paid);
            Assert.That(paid, Is.EqualTo(before - priced.Total), "The order should cost the quoted total.");

            // A confirmation can't be replayed.
            Assert.That(orders.PlaceOrder(vendor, buyer, quote.Value, priced.Total), Is.Null);
            bank.TryGetBalance(buyer, out var replay);
            Assert.That(replay, Is.EqualTo(paid));

            // Cancelling refunds every crate and puts the shelf back.
            Assert.That(orders.TryCancel(buyer, id.Value), Is.True);
            bank.TryGetBalance(buyer, out var refunded);
            Assert.That(refunded, Is.EqualTo(before), "A cancelled order refunds exactly what it charged.");
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(startGood));
            Assert.That(market.AvailableStock(grid, Other), Is.EqualTo(startOther));
            Assert.That(zones.FreeCount(area), Is.EqualTo(24), "Cancelling releases every reserved cell.");
            Assert.That(orders.TryGetOrder(id.Value, out _), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RefusalsLeaveMoneyStockAndCellsAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var bank = em.System<BankSystem>();

        EntityUid buyer = default!;

        await server.WaitAssertion(() =>
        {
            buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var profile = market.Profile(grid);
            var target = (int) profile.StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 3);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            Assert.That(zones.TryZone(zone, out var area), Is.True);

            var cart = new[] { new WGTradeBulkLine(Good, 4) };
            var priced = market.CartQuote(vendor, cart)!;

            bank.TryGetBalance(buyer, out var before);

            // A confirmation whose estimate doesn't match the server's is refused before anything moves.
            var stale = orders.Quote(vendor, buyer, cart, zone)!.Value;
            Assert.That(orders.PlaceOrder(vendor, buyer, stale, priced.Total - 1, out var staleError), Is.Null);
            Assert.That(staleError, Is.EqualTo("wg-bulk-error-price"));
            bank.TryGetBalance(buyer, out var afterStale);
            Assert.That(afterStale, Is.EqualTo(before));
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start));
            Assert.That(zones.FreeCount(area), Is.EqualTo(16));

            // The refused quote is spent, so the same id can't be confirmed later.
            Assert.That(orders.PlaceOrder(vendor, buyer, stale, priced.Total), Is.Null);

            // More than the shelf holds.
            market.SetVendorStock(grid, Good, 3);
            Assert.That(orders.Quote(vendor, buyer, cart, zone), Is.Null, "A cart can't exceed the shelf.");
            market.SetVendorStock(grid, Good, target);

            // No funds.
            bank.TryGetBalance(buyer, out var all);
            Assert.That(all, Is.GreaterThan(0));
            Assert.That(bank.TryBankWithdraw(buyer, all), Is.True);
            var broke = orders.Quote(vendor, buyer, cart, zone)!.Value;
            Assert.That(orders.PlaceOrder(vendor, buyer, broke, priced.Total, out var brokeError), Is.Null);
            Assert.That(brokeError, Is.EqualTo("wg-bulk-error-funds"));
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(start));
            Assert.That(zones.FreeCount(area), Is.EqualTo(16));

            // Prices moving between the estimate and the confirmation are named, not charged silently.
            market.SetVendorStock(grid, Good, target);
            bank.TryBankDeposit(buyer, priced.Total * 2, tax: false);
            var moved = orders.Quote(vendor, buyer, cart, zone)!.Value;
            market.Row(grid, Good).Priced = 0.5f;
            Assert.That(orders.PlaceOrder(vendor, buyer, moved, priced.Total, out var movedError), Is.Null);
            Assert.That(movedError, Is.EqualTo("wg-bulk-error-price"));
            market.SetVendorStock(grid, Good, target);

            // A destination on a grid far from the station.
            var far = BulkCargoTestWorld.Grid(em, map, new Vector2(2000, 2000), 3, 3);
            var farFirst = BulkCargoTestWorld.Marker(em, far, 0, 0);
            var farSecond = BulkCargoTestWorld.Marker(em, far, 2, 2);
            Assert.That(zones.TryPair(farFirst, farSecond, out var farZone), Is.True);
            Assert.That(orders.Quote(vendor, buyer, cart, farZone), Is.Null,
                "Crates only go to the vendor's station or ships docked to it.");

            // A ship beyond the landing does not count either.
            var farShip = BulkCargoTestWorld.Grid(em, map, new Vector2(600, 600), 3, 3);
            em.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(farShip);
            var shipFirst = BulkCargoTestWorld.Marker(em, farShip, 0, 0);
            var shipSecond = BulkCargoTestWorld.Marker(em, farShip, 2, 2);
            Assert.That(zones.TryPair(shipFirst, shipSecond, out var farShipZone), Is.True);
            Assert.That(orders.Quote(vendor, buyer, cart, farShipZone), Is.Null,
                "A ship far from the vendor is not a delivery destination.");

            // Too few free cells.
            var smallFirst = BulkCargoTestWorld.Marker(em, grid, 5, 5);
            var smallSecond = BulkCargoTestWorld.Marker(em, grid, 6, 6);
            Assert.That(zones.TryPair(smallFirst, smallSecond, out var small), Is.True);
            Assert.That(orders.Quote(vendor, buyer, new[] { new WGTradeBulkLine(Good, 5) }, small), Is.Null,
                "A zone with four tiles can't hold five crates.");

            // A good this vendor doesn't stock, and one that never runs out.
            Assert.That(orders.Quote(vendor, buyer, new[] { new WGTradeBulkLine("TradeGoodFissionFuel", 1) }, zone), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeliveredCrateDoesNotTakeStockTwice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();
        var purchase = em.System<VendingMachinePurchaseSystem>();

        EntityUid buyer = default!;

        await server.WaitAssertion(() =>
        {
            buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var profile = market.Profile(grid);
            var target = (int) profile.StockTarget;
            market.SetVendorStock(grid, Good, target);
            var start = market.AvailableStock(grid, Good);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 3);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);
            Assert.That(zones.TryZone(zone, out var area), Is.True);

            var cart = new[] { new WGTradeBulkLine(Good, 2) };
            var priced = market.CartQuote(vendor, cart)!;
            var quote = orders.Quote(vendor, buyer, cart, zone)!.Value;
            var id = orders.PlaceOrder(vendor, buyer, quote, priced.Total);
            Assert.That(id, Is.Not.Null);
            Assert.That(orders.TryGetOrder(id!.Value, out var order), Is.True);

            var left = market.AvailableStock(grid, Good);
            Assert.That(left, Is.EqualTo(start - 2));

            // The delivered crate records the sale; the shelf already lost those crates at payment.
            var crate = em.SpawnEntity(Good, new EntityCoordinates(grid, 0.5f, 0.5f));
            em.EnsureComponent<WGBulkPurchasedComponent>(crate);
            purchase.MarkAsPurchased(crate, vendor, order.Prices[0]);
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(left),
                "A bulk-delivered crate must not take stock off the shelf again.");

            // An ordinary vend still does.
            var ordinary = em.SpawnEntity(Good, new EntityCoordinates(grid, 1.5f, 1.5f));
            purchase.MarkAsPurchased(ordinary, vendor, order.Prices[0]);
            Assert.That(market.AvailableStock(grid, Good), Is.EqualTo(left - 1));

            // Leave no live queue behind for the pair to recycle around.
            Assert.That(orders.TryCancel(buyer, id.Value), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
