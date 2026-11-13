using Content.Server._WF.CargoSales;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>Tells Wolfgate systems what a pallet sale held.</summary>
    private void WfRaisePalletSale(HashSet<EntityUid> goods, EntityUid grid)
    {
        var ev = new WFPalletSaleEvent(goods, grid);
        RaiseLocalEvent(ref ev);
    }
}
