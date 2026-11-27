using Content.Server.Administration.Logs;
using Content.Server._NF.Bank;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._WG.BulkCargo;
using Content.Shared.Database;
using Content.Shared.VendingMachines;
using Robust.Shared.Player;
using System.Linq;

namespace Content.Server._WG.BulkCargo;

/// <summary>Confirmation and cancellation: the one transaction an order is charged in, and the refund when it stops.</summary>
public sealed partial class WGCargoBulkOrderSystem
{
    [Dependency] private IAdminLogManager _admin = default!;

    /// <summary>
    /// Confirms a held quote. Everything is checked before anything moves: the quote, the vendor's current shelf, the
    /// zone's free cells and the buyer's funds. Then the money is taken, stock comes off the shelf and the cells are
    /// reserved as one job. A failure after payment puts back whatever it took. Null if the order was refused.
    /// </summary>
    public Guid? PlaceOrder(EntityUid vendor, EntityUid buyer, Guid quoteId, int expectedTotal)
    {
        return PlaceOrder(vendor, buyer, quoteId, expectedTotal, out _);
    }

    /// <summary>
    /// The same transaction, with a localized reason when it is refused: an expired estimate, a price that moved, the
    /// shelf, the zone, funds or the payment. A caller can show the buyer which one it was.
    /// </summary>
    public Guid? PlaceOrder(EntityUid vendor, EntityUid buyer, Guid quoteId, int expectedTotal, out string? error)
    {
        error = null;
        if (!_quotes.Remove(quoteId, out var hold)
            || hold.Vendor != vendor
            || hold.Buyer != buyer)
        {
            error = "wg-bulk-error-quote";
            return null;
        }

        if (_actorQuote.TryGetValue(buyer, out var activeQuote) && activeQuote == quoteId)
            _actorQuote.Remove(buyer);

        if (hold.Expires < _timing.CurTime || hold.Total != expectedTotal)
        {
            error = hold.Expires < _timing.CurTime ? "wg-bulk-error-quote" : "wg-bulk-error-price";
            return null;
        }

        if (!_players.TryGetSessionByEntity(buyer, out var session)
            || HasComp<IronmanComponent>(buyer))
        {
            error = "wg-bulk-error-funds";
            return null;
        }

        if (ActiveOrders(buyer) >= MaxActiveOrdersPerBuyer)
        {
            error = "wg-bulk-error-many";
            return null;
        }

        if (Transform(vendor).GridUid is not { } grid)
        {
            error = "wg-bulk-error-zone";
            return null;
        }

        var counter = hold.Zone == WGCargoBulkDestinations.Counter;
        var area = default(WGCargoZone);
        var counterCells = new List<Vector2i>();
        if (counter)
        {
            counterCells = CounterCells(vendor, grid);
            if (counterCells.Count == 0)
            {
                error = "wg-bulk-error-zone";
                return null;
            }
        }
        else if (!_zones.TryZone(hold.Zone, out area) || !Destinations(vendor).Contains(area.Grid))
        {
            error = "wg-bulk-error-zone";
            return null;
        }

        // The shelf and its live modifiers decide the price again; a cart that moved is refused rather than charged
        // at a price the buyer never saw.
        var priced = _market.CartQuote(vendor, hold.Cart);
        if (priced is null)
        {
            error = "wg-bulk-error-stock";
            return null;
        }

        if (priced.Crates <= 0 || priced.Crates > MaxCratesPerOrder)
        {
            error = "wg-bulk-error-order";
            return null;
        }

        if (priced.Total != hold.Total)
        {
            error = "wg-bulk-error-price";
            return null;
        }

        if (!counter && _zones.FreeCount(area) < priced.Crates)
        {
            error = "wg-bulk-error-zone";
            return null;
        }

        if (!_bank.TryGetBalance(buyer, out var balance) || balance < priced.Total)
        {
            error = "wg-bulk-error-funds";
            return null;
        }

        if (!_bank.TryBankWithdraw(buyer, priced.Total))
        {
            error = "wg-bulk-error-funds";
            return null;
        }

        var taken = new List<(string Good, int Quantity)>(priced.Lines.Count);
        foreach (var line in priced.Lines)
        {
            if (!_market.TryTakeStock(grid, line.Good, line.Quantity))
            {
                Rollback(buyer, priced.Total, grid, taken);
                error = "wg-bulk-error-stock";
                return null;
            }

            taken.Add((line.Good, line.Quantity));
        }

        var id = Guid.NewGuid();
        var cells = new List<Vector2i>();
        if (!counter && !_zones.TryReserve(id, area, priced.Crates, out cells))
        {
            Rollback(buyer, priced.Total, grid, taken);
            error = "wg-bulk-error-zone";
            return null;
        }

        _market.RefreshStock(grid);

        var order = new WGCargoBulkOrder
        {
            Id = id,
            Vendor = vendor,
            VendorGrid = grid,
            Buyer = buyer,
            BuyerId = session.UserId,
            BuyerName = Name(buyer),
            Zone = hold.Zone,
            ZoneGrid = counter ? grid : area.Grid,
            ZoneLabel = counter
                ? Loc.GetString("wg-bulk-zone-counter", ("vendor", Name(vendor)))
                : ZoneLabel(grid, area),
            Cells = new List<Vector2i>(cells),
            CounterCells = counter ? new List<Vector2i>(counterCells) : new List<Vector2i>(),
            PlacedAt = _timing.CurTime,
            NextAt = _timing.CurTime,
        };

        foreach (var line in priced.Lines)
        {
            order.Goods.AddRange(Enumerable.Repeat(line.Good, line.Quantity));
            order.Prices.AddRange(line.Prices);
        }

        _orders[id] = order;
        if (!_byVendor.TryGetValue(vendor, out var orders))
        {
            orders = new List<Guid>();
            _byVendor[vendor] = orders;
        }

        orders.Add(id);

        PayTaxes(vendor, priced.Total);

        _admin.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(buyer):buyer} bulk ordered {priced.Crates} crates from {ToPrettyString(vendor):vendor} for {priced.Total} spesos to zone {hold.Zone}");

        return id;
    }

    /// <summary>
    /// Stops an order at its buyer's request, refunding the crates that never arrived at exactly what each cost and
    /// putting them back on the shelf. False if the buyer doesn't own it or the refund can't be paid, which leaves the
    /// order running rather than losing the money.
    /// </summary>
    public bool TryCancel(EntityUid buyer, Guid orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order)
            || order.Status != WGCargoBulkOrderStatus.Delivering
            || !_players.TryGetSessionByEntity(buyer, out var session)
            || session.UserId != order.BuyerId)
            return false;

        var refund = order.Refund;
        if (refund > 0 && !_bank.TryBankDeposit(order.Buyer, refund, tax: false))
            return false;

        Stop(order, refundPaid: true);
        return true;
    }

    /// <summary>Puts back a payment and any stock already taken when a confirmation fails partway.</summary>
    private void Rollback(EntityUid buyer, int paid, EntityUid grid, List<(string Good, int Quantity)> taken)
    {
        if (paid > 0)
            _bank.TryBankDeposit(buyer, paid, tax: false);

        if (TerminatingOrDeleted(grid))
            return;

        foreach (var (good, quantity) in taken)
            _market.ReturnStock(grid, good, quantity);

        if (taken.Count > 0)
            _market.RefreshStock(grid);
    }

    /// <summary>Pays each account the vendor prototype sends its cut to, as a single sale does.</summary>
    private void PayTaxes(EntityUid vendor, int total)
    {
        if (total <= 0 || !TryComp<VendingMachineComponent>(vendor, out var machine))
            return;

        foreach (var (account, taxCoeff) in machine.TaxAccounts)
        {
            if (!float.IsFinite(taxCoeff) || taxCoeff <= 0f)
                continue;

            _bank.TrySectorDeposit(account, (int) Math.Floor(total * taxCoeff), LedgerEntryType.VendorTax);
        }
    }

    /// <summary>How many paid orders one buyer still has in the delivery queue.</summary>
    private int ActiveOrders(EntityUid buyer)
    {
        var count = 0;
        foreach (var order in _orders.Values)
        {
            if (order.Buyer == buyer && order.Status == WGCargoBulkOrderStatus.Delivering)
                count++;
        }

        return count;
    }
}
