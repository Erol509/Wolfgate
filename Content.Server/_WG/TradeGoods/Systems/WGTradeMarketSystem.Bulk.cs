using Content.Shared._WG.TradeGoods;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>One line of a bulk cart: a good and how many crates of it.</summary>
public readonly record struct WGTradeBulkLine(string Good, int Quantity);

/// <summary>What one cart line costs: the crates walk the shelf curve down in the order they are taken.</summary>
public readonly record struct WGTradeBulkLineQuote(string Good, int Quantity, int First, int Last, int Subtotal, List<int> Prices);

/// <summary>A priced bulk cart: every line and the whole order.</summary>
public sealed class WGTradeBulkQuote
{
    public List<WGTradeBulkLineQuote> Lines = new();
    public int Crates;
    public int Total;
}

/// <summary>
/// Prices a cart of crates at a trade vendor before it is bought. Each crate costs the vendor's price at the shelf it
/// would leave behind, so a bulk order can't take a hundred crates at the first one's price. Stock comes off the shelf
/// only when the order is accepted; a quote never changes it.
/// </summary>
public sealed partial class WGTradeMarketSystem
{
    /// <summary>
    /// Whole crates of a good a station's vendors will sell now: its shelf above the level it keeps as scarce, so a bulk
    /// order cannot buy the reserve the upstream economy protects.
    /// </summary>
    public int AvailableStock(EntityUid grid, string good)
    {
        return ForSale(grid, good);
    }

    /// <summary>
    /// Prices a cart at one vendor. Each line is charged crate by crate at the level the shelf would stand at before
    /// that crate left it, never below the vendor's live single-crate price. Null when the vendor doesn't stock a good
    /// or the shelf can't fill a line.
    /// </summary>
    public WGTradeBulkQuote? CartQuote(EntityUid vendor, IReadOnlyList<WGTradeBulkLine> cart)
    {
        if (cart.Count == 0
            || !HasComp<WGTradeVendorComponent>(vendor)
            || Transform(vendor).GridUid is not { } grid
            || !TryComp<VendingMachineComponent>(vendor, out var machine))
            return null;

        var profile = Profile(grid);
        var quote = new WGTradeBulkQuote();
        var left = new Dictionary<string, int>(cart.Count);
        var shelf = new Dictionary<string, float>(cart.Count);

        foreach (var line in cart)
        {
            if (line.Quantity <= 0
                || profile.ContrabandGoods.Contains(line.Good)
                || !HasLimitedStock(line.Good)
                || !machine.Inventory.ContainsKey(line.Good)
                || !_proto.TryIndex<EntityPrototype>(line.Good, out var proto)
                || VendorBasePrice(vendor, proto) is not { } basePrice)
                return null;

            if (!left.TryGetValue(line.Good, out var available))
            {
                available = AvailableStock(grid, line.Good);
                left[line.Good] = available;
            }

            if (!shelf.TryGetValue(line.Good, out var stock))
            {
                stock = Row(grid, line.Good).Stock;
                shelf[line.Good] = stock;
            }

            if (line.Quantity > available)
                return null;

            var raise = BuyRaise(grid, line.Good, out _);
            var unit = BuyPriceAt(basePrice, profile, PricedLevel(grid, line.Good), raise);
            var prices = new List<int>(line.Quantity);
            var first = 0;
            var last = 0;
            var subtotal = 0;

            for (var i = 0; i < line.Quantity; i++)
            {
                // The shelf after i crates of this line have left it, reserve included.
                var level = MathF.Max(0f, (stock - i) / profile.StockTarget);
                var price = Math.Max(unit, BuyPriceAt(basePrice, profile, level, raise));
                if (i == 0)
                    first = price;
                last = price;
                subtotal += price;
                prices.Add(price);
            }

            left[line.Good] = available - line.Quantity;
            shelf[line.Good] = stock - line.Quantity;
            quote.Lines.Add(new WGTradeBulkLineQuote(line.Good, line.Quantity, first, last, subtotal, prices));
            quote.Crates += line.Quantity;
            quote.Total += subtotal;
        }

        return quote;
    }

    /// <summary>Takes whole crates off a station's shelf for a paid order, or false if the shelf can't fill it.</summary>
    public bool TryTakeStock(EntityUid grid, string good, int quantity)
    {
        if (quantity <= 0 || quantity > AvailableStock(grid, good))
            return false;

        AddStock(grid, good, -quantity);
        return true;
    }

    /// <summary>Puts whole crates back on a station's shelf when an order is refunded.</summary>
    public void ReturnStock(EntityUid grid, string good, int quantity)
    {
        if (quantity > 0)
            AddStock(grid, good, quantity);
    }

    /// <summary>Shows a station's stock and prices on its vendors now.</summary>
    public void RefreshStock(EntityUid grid)
    {
        RefreshVendors(grid);
    }

    /// <summary>The product of the modifiers that raise a buy price, and the reason of the largest as players see it.</summary>
    private float BuyRaise(EntityUid grid, string good, out LocId? reason)
    {
        var ev = new WGTradeBuyQuoteEvent(grid, good);
        RaiseLocalEvent(ev);

        var raise = 1f;
        var top = 1f;
        LocId? topReason = null;
        foreach (var modifier in ev.Modifiers)
        {
            if (modifier.Factor <= 1f)
                continue;

            raise *= modifier.Factor;
            if (modifier.Factor > top)
            {
                top = modifier.Factor;
                topReason = modifier.Reason;
            }
        }

        reason = topReason;
        return raise;
    }

    /// <summary>What one crate costs at a shelf level, with the stock curve floored at base as vendors charge it.</summary>
    private static int BuyPriceAt(int basePrice, WGTradeMarketPrototype profile, float level, float raise)
    {
        var stock = MathF.Max(1f, LevelFactor(profile, level));
        var factor = MathF.Min(stock * MathF.Min(raise, profile.ModifierCap), profile.PriceCap);
        return (int) MathF.Round(basePrice * factor);
    }
}
