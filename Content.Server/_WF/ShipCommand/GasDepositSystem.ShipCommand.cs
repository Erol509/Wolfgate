using Content.Server._NF.Atmos.Components;
using Content.Server._WF.ShipCommand;

namespace Content.Server._NF.Atmos.Systems;

/// <summary>A gas sale console aboard a commanded ship doesn't buy from the barred.</summary>
public sealed partial class GasDepositSystem
{
    [Dependency] private WFShipServiceSystem _wfShipServices = default!;

    /// <summary>True, with a popup, when the console's commanded ship turns the seller away; the gas stays in the sale points.</summary>
    private bool WfShipRefuses(Entity<GasSaleConsoleComponent> ent, EntityUid seller)
    {
        if (!_wfShipServices.Refuses(ent, seller, out var reason))
            return false;

        _popup.PopupEntity(reason, ent, seller);
        return true;
    }
}
