using Content.Shared.VendingMachines;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.BulkCargo;

/// <summary>The UI key a trade vendor opens for bulk crate orders.</summary>
[Serializable, NetSerializable]
public enum WGCargoBulkUiKey : byte
{
    Key,
}

/// <summary>Virtual destinations the bulk screen can choose in addition to placed zones.</summary>
public static class WGCargoBulkDestinations
{
    /// <summary>Dispense into the free floor tiles around the vendor, with no zone markers.</summary>
    public static readonly Guid Counter = new("c0a1b2c3-d4e5-4f60-8a7b-000000000001");
}

/// <summary>One line the vendor sells: a crate priced by the market, or the odd supply it vends normally.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkProduct
{
    public string Id = string.Empty;
    public int UnitPrice;
    public int Stock;

    /// <summary>True for a finite trade crate; false for an ordinary supply the vendor still vends one at a time.</summary>
    public bool Crate = true;

    public InventoryType Type = InventoryType.Regular;
}

/// <summary>One delivery zone the vendor can reach, with its tiles as the preview draws them.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkZone
{
    public Guid Id;
    public NetEntity Grid;
    public string Label = string.Empty;
    public bool Ship;
    public Vector2i Min;
    public Vector2i Max;
    public List<Vector2i> Free = new();
    public List<Vector2i> Blocked = new();
    public List<Vector2i> Reserved = new();

    /// <summary>Tiles holding crates a bulk order already delivered, so the preview keeps its fill after the order ends.</summary>
    public List<Vector2i> Landed = new();
}

/// <summary>The shared bulk order screen: what the vendor sells and where it can deliver.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkState : BoundUserInterfaceState
{
    public List<WGCargoBulkProduct> Products = new();
    public List<WGCargoBulkZone> Zones = new();
    public TimeSpan Interval = TimeSpan.FromSeconds(1.75);
    public int MaxCrates;
}

/// <summary>One product and how many of it are in an order.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkOrderLine
{
    public string Id = string.Empty;
    public int Count;
}

/// <summary>One of the viewer's own paid orders, as the order list and the preview show it.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkOrderEntry
{
    public Guid Id;
    public Guid Zone;
    public string ZoneLabel = string.Empty;
    public List<WGCargoBulkOrderLine> Lines = new();
    public List<Vector2i> Cells = new();
    public int Delivered;
    public int Total;
    public TimeSpan NextIn;
    public TimeSpan Interval;
}

/// <summary>What a cart would cost, line by line, or the reason it can't be filled.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkQuoteLine
{
    public string Id = string.Empty;
    public int Quantity;
    public int First;
    public int Last;
    public int Subtotal;
}

/// <summary>Asks the vendor to price a cart for one zone.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkQuoteRequestMessage : BoundUserInterfaceMessage
{
    public Dictionary<string, int> Items = new();
    public Guid Zone;

    /// <summary>Client-side request counter; a reply only applies while it is still the newest.</summary>
    public int Revision;
}

/// <summary>The vendor's answer: a single-use quote to confirm, or a localized reason it refused.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkQuoteMessage : BoundUserInterfaceMessage
{
    public Guid Quote;
    public List<WGCargoBulkQuoteLine> Lines = new();
    public int Total;
    public int Crates;
    public string? Error;

    /// <summary>The request revision this reply answers.</summary>
    public int Revision;
}

/// <summary>Confirms a quote at the total the buyer was shown.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkPlaceOrderMessage : BoundUserInterfaceMessage
{
    public Guid Quote;
    public int ExpectedTotal;
}

/// <summary>Whether an order went through, and why not.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkOrderResultMessage : BoundUserInterfaceMessage
{
    public bool Success;
    public string? Error;

    /// <summary>True for a placed order, false for a cancellation.</summary>
    public bool Placed;
}

/// <summary>Stops one of the viewer's orders.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkCancelMessage : BoundUserInterfaceMessage
{
    public Guid Order;
}

/// <summary>Asks for a zone projector to place the two markers.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkDispenseMessage : BoundUserInterfaceMessage
{
}

/// <summary>Asks the vendor to send the screen again, as a window opens.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkRefreshMessage : BoundUserInterfaceMessage
{
}

/// <summary>Whether a projector was handed over.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkDispenseResultMessage : BoundUserInterfaceMessage
{
    public bool Success;
    public string? Error;
}

/// <summary>The viewer's bank balance, sent apart from the shared state so only they see it.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkBalanceMessage : BoundUserInterfaceMessage
{
    public int Balance;
}

/// <summary>The viewer's own orders, with the cells the preview fills, sent apart from the shared state.</summary>
[Serializable, NetSerializable]
public sealed class WGCargoBulkOrdersMessage : BoundUserInterfaceMessage
{
    public List<WGCargoBulkOrderEntry> Orders = new();
}
