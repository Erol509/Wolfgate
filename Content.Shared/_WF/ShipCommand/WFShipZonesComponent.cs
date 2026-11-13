using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipCommand;

/// <summary>
/// A commanded ship's zones, each switched between Public, Staff, Locked and Sealed from the bridge. A door belongs
/// to a zone through its <c>WFDoorAccessRuleComponent.Zone</c>; switching the zone gives all of its doors the rule
/// the mode calls for. Saved with the grid, so a map carries its zones.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFShipZonesComponent : Component
{
    /// <summary>The ship's zones, in the order the console lists them.</summary>
    [DataField, AutoNetworkedField]
    public List<WFShipZone> Zones = new();
}

/// <summary>One zone: its id (what doors name), its display name, its mode and the tiles it covers.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WFShipZone
{
    /// <summary>The id its doors name in <c>WFDoorAccessRuleComponent.Zone</c>.</summary>
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>The Fluent id of its display name.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Who its doors admit now.</summary>
    [DataField]
    public WFShipZoneMode Mode = WFShipZoneMode.Public;

    /// <summary>
    /// The tiles the zone covers, as inclusive grid tile rectangles, for telling which zone someone stands in.
    /// Doors are members by their own field, not by these.
    /// </summary>
    [DataField]
    public List<Box2i> Areas = new();
}

/// <summary>Who a zone's doors admit.</summary>
[Serializable, NetSerializable]
public enum WFShipZoneMode : byte
{
    /// <summary>Everyone.</summary>
    Public,

    /// <summary>The owner, officers, the staff list and admins.</summary>
    Staff,

    /// <summary>The owner, officers and admins.</summary>
    Locked,

    /// <summary>Doors bolted shut; the owner and admins unseal it from the bridge.</summary>
    Sealed,
}

/// <summary>Raised on the grid after a zone's mode changed.</summary>
[ByRefEvent]
public readonly record struct WFShipZoneModeChangedEvent(string Zone, WFShipZoneMode Old, WFShipZoneMode New);
