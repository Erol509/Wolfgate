using Content.Server._Mono.VendingMachine;
using Content.Server.Power.EntitySystems;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared.Database;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// The delivery queue: one crate at a time from each paid order's tape, into the cell reserved for it. Every arrival is
/// re-checked against the live grid, marked as already paid for, and shown with the vendor's own effect and sound.
/// </summary>
public sealed partial class WGCargoBulkOrderSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private VendingMachinePurchaseSystem _purchase = default!;

    /// <summary>Settings for a vendor with no bulk component of its own.</summary>
    private static readonly WGCargoBulkVendorComponent Defaults = new();

    /// <summary>How long between attempts to pay a refund the buyer's account refused.</summary>
    private static readonly TimeSpan RefundRetry = TimeSpan.FromSeconds(10);

    /// <summary>How many refund attempts before a failed one is dropped and logged, so orders can't accumulate.</summary>
    private const int MaxRefundAttempts = 30;

    /// <summary>How long a blocked crate waits before the zone is scanned for a new cell again.</summary>
    private static readonly TimeSpan BlockedRetry = TimeSpan.FromSeconds(1);

    /// <summary>Shortest gap between two delivery-driven screen pushes to one vendor.</summary>
    private static readonly TimeSpan PushInterval = TimeSpan.FromMilliseconds(350);

    /// <summary>Reused snapshot of orders and vendors, so iterating never fights a callback that changes the sets.</summary>
    private readonly List<WGCargoBulkOrder> _deliverBuffer = new();
    private readonly List<EntityUid> _pushBuffer = new();

    /// <summary>Ticks every order: pays a waiting refund, then sends the next crate that is due.</summary>
    private void Deliver()
    {
        if (_orders.Count == 0)
            return;

        var now = _timing.CurTime;
        _deliverBuffer.Clear();
        _deliverBuffer.AddRange(_orders.Values);
        foreach (var order in _deliverBuffer)
        {
            if (order.Status == WGCargoBulkOrderStatus.Cancelled)
            {
                RetryRefund(order, now);
                continue;
            }

            if (order.Status != WGCargoBulkOrderStatus.Delivering || order.NextAt > now)
                continue;

            DeliverNext(order.Id);
        }
    }

    /// <summary>
    /// Sends one crate of an order now if it can: false when the order is gone, its vendor has no power, or its tiles
    /// are all blocked. The queue calls this when a crate is due; tools and tests may call it directly.
    /// </summary>
    public bool DeliverNext(Guid orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order) || order.Status != WGCargoBulkOrderStatus.Delivering)
            return false;

        if (TerminatingOrDeleted(order.Vendor) || TerminatingOrDeleted(order.ZoneGrid))
        {
            Stop(order, refundPaid: false);
            return false;
        }

        if (!this.IsPowered(order.Vendor, EntityManager))
        {
            // Don't re-check every tick while the vendor is dark.
            order.NextAt = _timing.CurTime + BlockedRetry;
            return false;
        }

        if (TryDeliver(order, _timing.CurTime))
        {
            SchedulePush(order.Vendor);
            return true;
        }

        Stop(order, refundPaid: false);
        SchedulePush(order.Vendor);
        return false;
    }

    /// <summary>Queues a vendor's screen for the next batched push.</summary>
    private void SchedulePush(EntityUid vendor)
    {
        _pendingPushes.Add(vendor);
    }

    /// <summary>Sends at most one screen update per vendor per interval, however many crates arrived.</summary>
    private void PushDue()
    {
        if (_pendingPushes.Count == 0 || _timing.CurTime < _nextPush)
            return;

        _nextPush = _timing.CurTime + PushInterval;

        // A push can close a stale zone and stop an order, which schedules another push; keep those for the next cycle.
        _pushBuffer.Clear();
        _pushBuffer.AddRange(_pendingPushes);
        _pendingPushes.Clear();

        foreach (var vendor in _pushBuffer)
            PushAll(vendor);
    }

    /// <summary>Sends the next due crate; false when the order can't go on and must be refunded.</summary>
    private bool TryDeliver(WGCargoBulkOrder order, TimeSpan now)
    {
        if (!TryComp<MapGridComponent>(order.ZoneGrid, out var gridComp)
            || !_zones.TryZone(order.Zone, out var area)
            || area.Grid != order.ZoneGrid)
            return false;

        if (order.Delivered >= order.Goods.Count)
        {
            Finish(order);
            return true;
        }

        var tile = order.Cells[order.Delivered];
        if (tile.X < area.Min.X || tile.X > area.Max.X || tile.Y < area.Min.Y || tile.Y > area.Max.Y)
            return false;

        if (_zones.ClassifyCell(order.ZoneGrid, tile, order.Id) != WGCargoCellState.Free)
        {
            if (!TryReplaceCell(order, out tile))
            {
                // Nothing free right now. Re-scanning the zone is expensive, so only retry about once a second, and not
                // forever.
                order.BlockedSince ??= now;
                order.NextAt = now + BlockedRetry;
                return now - order.BlockedSince < Bulk(order.Vendor).BlockedTimeout;
            }

            order.Cells[order.Delivered] = tile;
        }

        var settings = Bulk(order.Vendor);
        var coords = _map.GridTileToLocal(order.ZoneGrid, gridComp, tile);
        var crate = Spawn(order.Goods[order.Delivered], coords);

        // The shelf already lost this crate when the order was paid for. The marker must exist before
        // MarkAsPurchased adds VendingMachinePurchaseComponent: its ComponentStartup hook (OnPurchased) only skips the
        // second stock debit when it sees this marker.
        EnsureComp<WGBulkPurchasedComponent>(crate);
        _purchase.MarkAsPurchased(crate, order.Vendor, order.Prices[order.Delivered]);

        Spawn(settings.TeleportEffect, coords);
        _audio.PlayPvs(settings.TeleportSound, coords);

        order.Delivered++;
        order.BlockedSince = null;
        _zones.ReleaseCell(order.Id, order.ZoneGrid, tile);

        if (order.Delivered >= order.Goods.Count)
            Finish(order);
        else
            order.NextAt = now + settings.DeliveryInterval;

        return true;
    }

    /// <summary>
    /// Moves a crate whose reserved tile turned unusable to another free tile of its zone, or false if the zone is gone
    /// or full.
    /// </summary>
    private bool TryReplaceCell(WGCargoBulkOrder order, out Vector2i tile)
    {
        tile = default;
        if (!_zones.TryZone(order.Zone, out var zone) || zone.Grid != order.ZoneGrid)
            return false;

        for (var y = zone.Min.Y; y <= zone.Max.Y; y++)
        {
            for (var x = zone.Min.X; x <= zone.Max.X; x++)
            {
                var candidate = new Vector2i(x, y);
                if (order.Cells.Contains(candidate))
                    continue;

                if (_zones.Classify(zone, candidate, order.Id) != WGCargoCellState.Free)
                    continue;

                if (!_zones.TryReserveCell(order.Id, zone.Grid, candidate))
                    continue;

                _zones.ReleaseCell(order.Id, order.ZoneGrid, order.Cells[order.Delivered]);
                tile = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Every crate arrived.</summary>
    private void Finish(WGCargoBulkOrder order)
    {
        _zones.Release(order.Id);
        order.Status = WGCargoBulkOrderStatus.Done;
        _orders.Remove(order.Id);
        if (_byVendor.TryGetValue(order.Vendor, out var orders))
            orders.Remove(order.Id);

        _admin.Add(LogType.Action, LogImpact.Low,
            $"Bulk order {order.Id} delivered all {order.Goods.Count} crates for {ToPrettyString(order.Buyer)}");
    }

    /// <summary>
    /// Ends an order early: returns its undelivered crates to the shelf, releases its cells, and pays back what they cost
    /// at the prices the buyer saw. A refund the account refuses is retried until it lands.
    /// </summary>
    private void Stop(WGCargoBulkOrder order, bool refundPaid)
    {
        // TryZone can close a stale zone and ask for its orders to stop; never refund or restock twice.
        if (order.Status != WGCargoBulkOrderStatus.Delivering)
            return;

        var goods = new Dictionary<string, int>();
        for (var i = order.Delivered; i < order.Goods.Count; i++)
            goods[order.Goods[i]] = goods.GetValueOrDefault(order.Goods[i]) + 1;

        // The station may already be gone, in which case there is no shelf to return anything to.
        if (!TerminatingOrDeleted(order.VendorGrid))
        {
            foreach (var (good, quantity) in goods)
                _market.ReturnStock(order.VendorGrid, good, quantity);

            if (goods.Count > 0)
                _market.RefreshStock(order.VendorGrid);
        }

        _zones.Release(order.Id);
        order.Status = WGCargoBulkOrderStatus.Cancelled;
        if (_byVendor.TryGetValue(order.Vendor, out var orders))
            orders.Remove(order.Id);

        var refund = order.Refund;
        if (!refundPaid
            && refund > 0
            && (TerminatingOrDeleted(order.Buyer) || !_bank.TryBankDeposit(order.Buyer, refund, tax: false)))
        {
            order.PendingRefund = refund;
            order.NextRefundAt = _timing.CurTime + RefundRetry;
            order.RefundAttempts = 0;
            _admin.Add(LogType.Action, LogImpact.Medium,
                $"Bulk order {order.Id} stopped with {order.Goods.Count - order.Delivered} crates undelivered; {refund} spesos owed to {ToPrettyString(order.Buyer)}");
            return;
        }

        _orders.Remove(order.Id);
        _admin.Add(LogType.Action, LogImpact.Medium,
            $"Bulk order {order.Id} stopped with {order.Goods.Count - order.Delivered} crates refunded ({refund} spesos)");
    }

    /// <summary>Pays a refund the buyer's account refused when the order stopped.</summary>
    private void RetryRefund(WGCargoBulkOrder order, TimeSpan now)
    {
        if (order.NextRefundAt > now)
            return;

        // A deleted body can never take the money, and a round can't keep retrying forever.
        if (TerminatingOrDeleted(order.Buyer) || ++order.RefundAttempts > MaxRefundAttempts)
        {
            _orders.Remove(order.Id);
            _admin.Add(LogType.Action, LogImpact.High,
                $"Bulk order {order.Id} dropped with {order.PendingRefund} spesos unrefunded: {order.BuyerName} could not be paid");
            return;
        }

        if (!_bank.TryBankDeposit(order.Buyer, order.PendingRefund, tax: false))
        {
            order.NextRefundAt = now + RefundRetry;
            return;
        }

        order.PendingRefund = 0;
        _orders.Remove(order.Id);
    }

    /// <summary>A vendor's delivery settings, or the defaults when its prototype has none.</summary>
    private WGCargoBulkVendorComponent Bulk(EntityUid vendor)
    {
        return TryComp<WGCargoBulkVendorComponent>(vendor, out var settings) ? settings : Defaults;
    }
}
