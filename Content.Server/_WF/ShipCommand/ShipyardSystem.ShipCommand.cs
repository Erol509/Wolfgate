using System.Linq;
using Content.Server._WF.ShipCommand;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.ShipCommand;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>Shipyards aboard a commanded ship charge the ship's prices.</summary>
public sealed partial class ShipyardSystem
{
    /// <summary>Stands in for a list price when asking what a rule makes of it.</summary>
    private const int WfProbePrice = 1_000_000;

    [Dependency] private WFShipServiceSystem _wfShipServices = default!;

    /// <summary>True, with a popup and the deny sound, when a shipyard aboard a commanded ship turns the customer away.</summary>
    private bool WfRefuses(EntityUid console, EntityUid customer, ShipyardConsoleComponent component)
    {
        if (!_wfShipServices.Refuses(console, customer, out var reason))
            return false;

        ConsolePopup(customer, reason);
        PlayDenySound(customer, console, component);
        return true;
    }

    /// <summary>What a buyer pays for a ship here, and the rule that set it; the list price off a commanded ship.</summary>
    private int WfQuote(EntityUid console, EntityUid buyer, int listPrice, out string? rule)
    {
        return _wfShipServices.Quote(console, buyer, WFShipServiceKind.Shipyard, listPrice, out rule);
    }

    /// <summary>
    /// The prices shown at this console against the list price; 1 off a commanded ship. Everyone watching shares one
    /// state, so it is the highest any of them would pay: nobody is shown a bargain they won't get.
    /// </summary>
    private float WfPriceMultiplier(EntityUid console, ShipyardConsoleUiKey key)
    {
        var quotes = _ui.GetActors(console, key).Select(viewer => _wfShipServices.Quote(console, viewer, WFShipServiceKind.Shipyard, WfProbePrice));
        return quotes.DefaultIfEmpty(WfProbePrice).Max() / (float) WfProbePrice;
    }

    /// <summary>A shipyard's own share of the service log, and the bargain mark on what was bought; a voucher sale is neither.</summary>
    private void WfRecordPurchase(EntityUid console, EntityUid buyer, EntityUid shuttle, string vessel, int listPrice, int charged, string? rule)
    {
        _wfShipServices.Record(console, buyer, WFShipServiceKind.Shipyard, vessel, listPrice, charged, rule);
        _wfShipServices.MarkBargain(shuttle, listPrice, charged);
    }
}
