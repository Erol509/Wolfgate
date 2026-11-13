using Content.Shared._WF.ShipCommand;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// The round's record of service use aboard a commanded ship, newest last and capped; the admin log keeps every
/// line for good. Server only, round state.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipServiceLedgerComponent : Component
{
    [ViewVariables]
    public List<WFShipServiceEntry> Entries = new();
}

/// <summary>One use of a service: when (round time), who, which service and what, the usual price, what was charged and the rule.</summary>
public readonly record struct WFShipServiceEntry(
    TimeSpan Time,
    string Customer,
    WFShipServiceKind Kind,
    string Service,
    string What,
    int BasePrice,
    int Charged,
    string? Rule);
