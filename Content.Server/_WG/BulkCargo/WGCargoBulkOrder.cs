using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._WG.BulkCargo;

/// <summary>Where a bulk order stands.</summary>
public enum WGCargoBulkOrderStatus : byte
{
    /// <summary>Paid for, with crates still to come.</summary>
    Delivering,

    /// <summary>Every crate was delivered.</summary>
    Done,

    /// <summary>Stopped, with what was not delivered refunded.</summary>
    Cancelled,
}

/// <summary>
/// A paid bulk order: one entry per crate in delivery order, the cells reserved for them, and what has arrived so far.
/// The tape is frozen when the order is accepted, so a refund always pays back exactly what each crate cost.
/// </summary>
public sealed class WGCargoBulkOrder
{
    public Guid Id;

    /// <summary>The vendor that sold it, and the station whose shelf it came off.</summary>
    public EntityUid Vendor;
    public EntityUid VendorGrid;

    /// <summary>Who paid and where a refund goes.</summary>
    public EntityUid Buyer;
    public NetUserId BuyerId;
    public string BuyerName = string.Empty;

    /// <summary>The zone and the grid it sits on; a ship zone keeps to its ship after undocking.</summary>
    public Guid Zone;
    public EntityUid ZoneGrid;
    public string ZoneLabel = string.Empty;

    /// <summary>One product and its price per crate, in delivery order.</summary>
    public List<string> Goods = new();
    public List<int> Prices = new();

    /// <summary>The reserved cells, in delivery order.</summary>
    public List<Vector2i> Cells = new();

    /// <summary>Floor tiles beside the vendor for a counter order; empty for zone orders.</summary>
    public List<Vector2i> CounterCells = new();

    public int Delivered;
    public WGCargoBulkOrderStatus Status = WGCargoBulkOrderStatus.Delivering;

    public TimeSpan PlacedAt;
    public TimeSpan NextAt;

    /// <summary>When the next crate first found its tile unusable, or null while it has a place to go.</summary>
    public TimeSpan? BlockedSince;

    /// <summary>What the undelivered crates would refund.</summary>
    public int Refund
    {
        get
        {
            var refund = 0;
            for (var i = Delivered; i < Prices.Count; i++)
                refund += Prices[i];

            return refund;
        }
    }
}
