#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server._WG.BulkCargo;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.BulkCargo;
using Content.Shared._WG.TradeGoods;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.BulkCargo;

/// <summary>
/// The trade vendor's bulk screen: a priced catalogue, the station's delivery zones with their tiles, the viewer's own
/// orders with the cells the preview fills, and a projector handed over from the counter.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCargoBulkOrderSystem))]
public sealed class BulkVendorUiTest
{
    private const string Good = BulkCargoTestWorld.Fabric;

    [Test]
    public async Task VendorScreenListsProductsAndZones()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var market = em.System<WGTradeMarketSystem>();
        var zones = em.System<WGCargoDeliveryZoneSystem>();
        var orders = em.System<WGCargoBulkOrderSystem>();

        EntityUid buyer = default!;
        Guid orderId = default;
        await server.WaitAssertion(() =>
        {
            buyer = BulkCargoTestWorld.Payer(pair, map.MapId);
            var grid = map.Grid.Owner;
            BulkCargoTestWorld.Floor(em, grid, map.Tile.Tile, 12, 12);
            var vendor = em.SpawnEntity(BulkCargoTestWorld.Vendor, new EntityCoordinates(grid, 9.5f, 9.5f));
            var power = em.EnsureComponent<ApcPowerReceiverComponent>(vendor);
            power.NeedsPower = false;
            power.PowerDisabled = false;
            power.Powered = true;
            em.System<SharedTransformSystem>().SetCoordinates(buyer, new EntityCoordinates(grid, 8.5f, 9.5f));

            var target = (int) market.Profile(grid).StockTarget;
            market.SetVendorStock(grid, Good, target);

            var first = BulkCargoTestWorld.Marker(em, grid, 0, 0);
            var second = BulkCargoTestWorld.Marker(em, grid, 3, 3);
            Assert.That(zones.TryPair(first, second, out var zone), Is.True);

            // A ship parked at the station has a zone of its own to choose.
            var ship = BulkCargoTestWorld.Grid(em, map, new Vector2(30, 0), 4, 4);
            em.EnsureComponent<ShuttleComponent>(ship);
            var shipFirst = BulkCargoTestWorld.Marker(em, ship, 0, 0);
            var shipSecond = BulkCargoTestWorld.Marker(em, ship, 2, 2);
            Assert.That(zones.TryPair(shipFirst, shipSecond, out var shipZone), Is.True);

            // The trade vendor now opens this UI rather than the one-item vending menu.
            var ui = em.System<UserInterfaceSystem>();
            Assert.That(ui.HasUi(vendor, WGCargoBulkUiKey.Key), Is.True);
            Assert.That(ui.TryOpenUi(vendor, WGCargoBulkUiKey.Key, buyer), Is.True);

            var state = orders.BuildState(vendor);
            var product = state.Products.FirstOrDefault(entry => entry.Id == Good);
            Assert.That(product, Is.Not.Null, "The vendor should list its trade goods.");
            Assert.That(product!.UnitPrice, Is.EqualTo(market.BuyQuote(vendor, Good)!.Value.Amount));
            Assert.That(product.Stock, Is.EqualTo(market.AvailableStock(grid, Good)));
            Assert.That(state.Interval, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(state.MaxCrates, Is.GreaterThan(0));

            Assert.That(state.Zones, Has.Count.EqualTo(3));
            Assert.That(state.Zones.Any(entry => entry.Id == WGCargoBulkDestinations.Counter), Is.True,
                "The counter is always a destination, even without a marked zone.");
            var screen = state.Zones.First(entry => entry.Id == zone);
            Assert.That(screen.Id, Is.EqualTo(zone));
            Assert.That(screen.Grid, Is.EqualTo(em.GetNetEntity(grid)));
            Assert.That(screen.Ship, Is.False);
            Assert.That(screen.Min, Is.EqualTo(new Vector2i(0, 0)));
            Assert.That(screen.Max, Is.EqualTo(new Vector2i(3, 3)));
            Assert.That(screen.Free, Has.Count.EqualTo(16), "The preview should have every free tile.");
            Assert.That(screen.Blocked, Is.Empty);
            Assert.That(screen.Label, Is.Not.Empty);

            var shipScreen = state.Zones.First(zone => zone.Id == shipZone);
            Assert.That(shipScreen.Ship, Is.True, "A zone on a nearby ship should be offered.");
            Assert.That(shipScreen.Free, Has.Count.EqualTo(9));
            Assert.That(shipScreen.Label, Does.Contain("hold"));

            // Place an order and read it back as the viewer's own, with the cells the preview fills.
            var cart = new[] { new WGTradeBulkLine(Good, 3) };
            var priced = market.CartQuote(vendor, cart)!;
            orderId = orders.PlaceOrder(vendor, buyer, orders.Quote(vendor, buyer, cart, zone)!.Value, priced.Total)!.Value;

            var own = orders.ViewerOrders(vendor, buyer);
            Assert.That(own, Has.Count.EqualTo(1));
            Assert.That(own[0].Cells, Has.Count.EqualTo(3));
            Assert.That(own[0].Total, Is.EqualTo(3));
            Assert.That(own[0].Delivered, Is.Zero);
            Assert.That(own[0].ZoneLabel, Is.Not.Empty);
            Assert.That(own[0].Lines.Sum(line => line.Count), Is.EqualTo(3));

            // A projector comes over the counter once, then waits.
            Assert.That(orders.TryDispenseProjector(vendor, buyer, out var error), Is.True);
            Assert.That(error, Is.Null);
            Assert.That(em.EntityQuery<WGCargoZoneProjectorComponent>().Any(), Is.True);
            Assert.That(orders.TryDispenseProjector(vendor, buyer, out var refused), Is.False);
            Assert.That(refused, Is.Not.Null);

            // Deliver the order: the preview keeps its landed crates after the order ends.
            for (var i = 0; i < 3; i++)
                Assert.That(orders.DeliverNext(orderId), Is.True);

            Assert.That(orders.TryGetOrder(orderId, out _), Is.False);
            var delivered = orders.BuildState(vendor);
            var landed = delivered.Zones.First(entry => entry.Id == zone);
            Assert.That(landed.Landed, Has.Count.EqualTo(3), "Landed crates should stay drawn in the zone.");
        });

        await pair.CleanReturnAsync();
    }
}
