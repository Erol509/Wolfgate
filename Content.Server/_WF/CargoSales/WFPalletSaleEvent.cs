namespace Content.Server._WF.CargoSales;

/// <summary>Raised when a cargo pallet sale goes through, with everything sold and all it contained, before any of it is deleted.</summary>
[ByRefEvent]
public readonly record struct WFPalletSaleEvent(HashSet<EntityUid> Goods, EntityUid Grid);
