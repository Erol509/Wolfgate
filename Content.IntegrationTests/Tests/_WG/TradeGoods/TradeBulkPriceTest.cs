#nullable enable
using System;
using System.Collections.Generic;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.VendingMachines;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// A bulk cart is priced crate by crate down the shelf curve, never below what the vendor charges for one, and with the
/// station's own event modifiers applied once rather than per crate.
/// </summary>
[TestFixture]
[TestOf(typeof(WGTradeMarketSystem))]
public sealed class TradeBulkPriceTest
{
    private const string Vendor = "VendingMachineTraderDepot";
    private const string Fabric = TradeTestWorld.Fabric;
    private const string Ammo = "TradeGoodAmmo";
    private const string FissionFuel = "TradeGoodFissionFuel";

    private static WGTradeBulkLine[] Line(string good, int quantity)
    {
        return new[] { new WGTradeBulkLine(good, quantity) };
    }

    [Test]
    public async Task CartWalksTheShelfDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            market.SetVendorStock(grid, Fabric, (int) profile.StockTarget);
            var available = market.AvailableStock(grid, Fabric);
            Assert.That(available, Is.GreaterThan(0));

            var unit = market.BuyQuote(vendor, Fabric)!.Value.Amount;
            Assert.That(unit, Is.GreaterThan(0));

            var one = market.CartQuote(vendor, Line(Fabric, 1))!;
            Assert.That(one.Crates, Is.EqualTo(1));
            Assert.That(one.Total, Is.EqualTo(unit), "One crate in a cart should cost one crate at the vendor.");
            Assert.That(one.Lines[0].First, Is.EqualTo(unit));
            Assert.That(one.Lines[0].Last, Is.EqualTo(unit));

            var nine = market.CartQuote(vendor, Line(Fabric, 9))!;
            var ten = market.CartQuote(vendor, Line(Fabric, 10))!;
            Assert.That(ten.Lines[0].First, Is.EqualTo(unit), "The first crate should cost the listed price.");
            Assert.That(ten.Lines[0].Last, Is.GreaterThan(unit), "Later crates should cost more as the shelf empties.");
            Assert.That(ten.Total, Is.GreaterThan(10 * unit), "A cart must not charge the first crate's price for all of them.");
            Assert.That(ten.Total - nine.Total, Is.EqualTo(ten.Lines[0].Last), "Each crate should be priced on its own.");
            Assert.That(ten.Total - nine.Total, Is.GreaterThanOrEqualTo(nine.Lines[0].Last));

            // A full saleable hold: the curve climbs toward the reserved scarcity level, under the station's price cap.
            var many = market.CartQuote(vendor, Line(Fabric, available))!;
            Assert.That(many.Crates, Is.EqualTo(available));
            Assert.That(many.Lines[0].Last, Is.GreaterThan(many.Lines[0].First));
            Assert.That(many.Lines[0].Last, Is.LessThanOrEqualTo((int) (unit * profile.PriceCap)));

            // Quoting leaves the shelf alone.
            Assert.That(market.AvailableStock(grid, Fabric), Is.EqualTo(available));

            // The vendor's own menu price for one crate is unchanged by bulk pricing.
            Assert.That(market.BuyPrice(vendor, server.ProtoMan.Index<EntityPrototype>(Fabric), unit), Is.EqualTo(unit));
            Assert.That(entMan.GetComponent<WGTradeVendorComponent>(vendor).Prices[Fabric], Is.EqualTo(unit));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CartIsBoundedByStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            var profile = market.Profile(grid);
            var reserve = (int) (profile.StockTarget * profile.ScarceLevel);
            market.SetVendorStock(grid, Fabric, reserve + 5);

            Assert.That(market.AvailableStock(grid, Fabric), Is.EqualTo(5));
            Assert.That(market.CartQuote(vendor, Line(Fabric, 5)), Is.Not.Null);
            Assert.That(market.CartQuote(vendor, Line(Fabric, 6)), Is.Null, "A cart can't order more than the shelf sells.");

            // A good the vendor doesn't stock, or one that never runs out, isn't sold by the crate.
            Assert.That(market.CartQuote(vendor, Line(FissionFuel, 1)), Is.Null);
            Assert.That(market.CartQuote(vendor, Line(Fabric, 0)), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CartAppliesModifiersOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();
        var shortages = entMan.System<WGTradeShortageSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            market.SetVendorStock(grid, Fabric, (int) profile.StockTarget);

            var plain = market.CartQuote(vendor, Line(Fabric, 10))!;

            // A local shortage doubles this good's price, once, on every crate of the cart.
            shortages.StartLocal(grid, new HashSet<string> { Fabric }, 2f, TimeSpan.FromMinutes(5), "fabric");
            var shorted = market.CartQuote(vendor, Line(Fabric, 10))!;
            shortages.Clear();

            Assert.That(shorted.Lines[0].First, Is.EqualTo(2 * plain.Lines[0].First));
            Assert.That(shorted.Lines[0].Last, Is.EqualTo(2 * plain.Lines[0].Last).Within(2),
                "The shortage should scale the curve, not compound crate by crate.");
            Assert.That(shorted.Total, Is.EqualTo(2 * plain.Total).Within(20));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CartSumsItsLines()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            market.SetVendorStock(grid, Fabric, (int) profile.StockTarget);
            market.SetVendorStock(grid, Ammo, (int) profile.StockTarget);

            var fabric = market.CartQuote(vendor, Line(Fabric, 10))!;
            var ammo = market.CartQuote(vendor, Line(Ammo, 5))!;
            var both = market.CartQuote(vendor, new[]
            {
                new WGTradeBulkLine(Fabric, 10),
                new WGTradeBulkLine(Ammo, 5),
            })!;

            Assert.That(both.Crates, Is.EqualTo(15));
            Assert.That(both.Total, Is.EqualTo(fabric.Total + ammo.Total), "A cart should cost the sum of its lines.");

            // Two lines of one good walk the same shelf in order, so they equal one merged line.
            var split = market.CartQuote(vendor, new[]
            {
                new WGTradeBulkLine(Fabric, 5),
                new WGTradeBulkLine(Fabric, 5),
            })!;
            Assert.That(split.Total, Is.EqualTo(fabric.Total), "Split lines of one good should price as one line.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CartNeverUndercutsTheVendor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);

            // A lagging price above the shelf, as a station left dear by a past shortage: the cart still charges at
            // least the live single-crate price and walks the real shelf from there.
            market.SetVendorStock(grid, Fabric, (int) (profile.StockTarget * 0.5f));
            market.Row(grid, Fabric).Priced = 1.4f;
            var live = market.BuyQuote(vendor, Fabric)!.Value.Amount;
            var cart = market.CartQuote(vendor, Line(Fabric, 10))!;
            Assert.That(cart.Lines[0].First, Is.GreaterThanOrEqualTo(live));
            Assert.That(cart.Total, Is.GreaterThanOrEqualTo(10 * live));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StockComesOffAndGoesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            var profile = market.Profile(grid);
            var target = (int) profile.StockTarget;
            var vendor = entMan.SpawnEntity(Vendor, map.GridCoords);
            market.SetVendorStock(grid, Fabric, target);
            var available = market.AvailableStock(grid, Fabric);

            Assert.That(market.TryTakeStock(grid, Fabric, 10), Is.True);
            market.RefreshStock(grid);
            Assert.That(market.AvailableStock(grid, Fabric), Is.EqualTo(available - 10),
                "A paid order should come off what the shelf sells at once.");
            Assert.That(MenuStock(entMan, vendor, Fabric), Is.EqualTo((uint) (available - 10)),
                "The vendor's menu should show what the station has for sale left.");

            Assert.That(market.TryTakeStock(grid, Fabric, available), Is.False, "The shelf can't sell its reserve.");
            Assert.That(market.AvailableStock(grid, Fabric), Is.EqualTo(available - 10));

            market.ReturnStock(grid, Fabric, 10);
            market.RefreshStock(grid);
            Assert.That(market.AvailableStock(grid, Fabric), Is.EqualTo(available), "A refund should put the crates back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>One vendor entry's amount, for the stock the vending menu reads.</summary>
    private static uint MenuStock(IEntityManager entMan, EntityUid vendor, string good)
    {
        return entMan.GetComponent<Content.Shared.VendingMachines.VendingMachineComponent>(vendor).Inventory[good].Amount;
    }
}
