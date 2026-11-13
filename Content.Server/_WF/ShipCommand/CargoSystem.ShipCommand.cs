using Content.Server._WF.ShipCommand;

namespace Content.Server.Cargo.Systems;

/// <summary>Cargo order consoles and sale pallets aboard a commanded ship turn the barred away.</summary>
public sealed partial class CargoSystem
{
    [Dependency] private WFShipServiceSystem _wfShipServices = default!;

    /// <summary>True, with a popup, when a cargo console aboard a commanded ship turns the customer away.</summary>
    private bool WfShipRefuses(EntityUid console, EntityUid customer)
    {
        if (!_wfShipServices.Refuses(console, customer, out var reason))
            return false;

        _popup.PopupEntity(reason, console, customer);
        return true;
    }
}
