namespace Content.Server._WG.BulkCargo;

/// <summary>
/// A crate delivered by a bulk order. Its stock left the station's shelf when the order was paid for, so the purchase
/// hook must not take it off again when the crate appears.
/// </summary>
[RegisterComponent]
public sealed partial class WGBulkPurchasedComponent : Component;
