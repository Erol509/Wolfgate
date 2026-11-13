using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipCommand;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's settings as a preset stores them, as JSON in the database: zone modes, the command lists by
/// player, the admin powers and crew responses, price rules, the roster's restaffing and stood-down posts, the
/// callsign, the crew's standing orders and the open job slots. People are kept by player, since cards belong to one round.
/// </summary>
public sealed class WFShipPresetData
{
    /// <summary>The format's version, for reading older presets.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Each zone's mode, by zone id.</summary>
    public Dictionary<string, WFShipZoneMode> Zones { get; set; } = new();

    /// <summary>The officers list, by player.</summary>
    public List<WFShipPresetPerson> Officers { get; set; } = new();

    /// <summary>The staff list, by player.</summary>
    public List<WFShipPresetPerson> Staff { get; set; } = new();

    /// <summary>The barred list, by player.</summary>
    public List<WFShipPresetPerson> Barred { get; set; } = new();

    /// <summary>See <see cref="WFShipCommandComponent.AdminsInZones"/>.</summary>
    public bool AdminsInZones { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.AdminsAtConsole"/>.</summary>
    public bool AdminsAtConsole { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.AdminsNeverTargeted"/>.</summary>
    public bool AdminsNeverTargeted { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.AdminsServedFree"/>.</summary>
    public bool AdminsServedFree { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.EventStaffAreAdmins"/>.</summary>
    public bool EventStaffAreAdmins { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.OwnerServedFree"/>.</summary>
    public bool OwnerServedFree { get; set; } = true;

    /// <summary>See <see cref="WFShipCommandComponent.DocksWelcome"/>.</summary>
    public bool DocksWelcome { get; set; } = true;

    /// <summary>The crew's answer to someone in a Staff zone who may not be there.</summary>
    public WFCrewSecurityResponse StaffZoneResponse { get; set; } = WFCrewSecurityResponse.Warn;

    /// <summary>The crew's answer to someone in a Locked zone who may not be there.</summary>
    public WFCrewSecurityResponse LockedZoneResponse { get; set; } = WFCrewSecurityResponse.Warn;

    /// <summary>The crew's answer to someone inside a Sealed zone.</summary>
    public WFCrewSecurityResponse SealedZoneResponse { get; set; } = WFCrewSecurityResponse.Hostile;

    /// <summary>The crew's answer to someone on the barred list.</summary>
    public WFCrewSecurityResponse BarredResponse { get; set; } = WFCrewSecurityResponse.Warn;

    /// <summary>The price rules; a rule for one person keeps them by player.</summary>
    public List<WFShipPresetPriceRule> PriceRules { get; set; } = new();

    /// <summary>Lost posts are restaffed on their own.</summary>
    public bool AutoRestaff { get; set; }

    /// <summary>How long a post stays empty before automatic restaffing fills it, in seconds.</summary>
    public int RestaffDelaySeconds { get; set; } = 120;

    /// <summary>The most automatic restaffs in an hour.</summary>
    public int RestaffsPerHour { get; set; } = 6;

    /// <summary>The ids of the posts stood down.</summary>
    public List<string> StoodDown { get; set; } = new();

    /// <summary>The radio officer's callsign; empty for the ship's own name.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Where the marines stand when nothing is happening.</summary>
    public WFShipMarineOrders MarineOrders { get; set; } = WFShipMarineOrders.Patrol;

    /// <summary>When the gunners open fire without being given a target.</summary>
    public WFGunnerPolicy GunnerPolicy { get; set; } = WFGunnerPolicy.Hostiles;

    /// <summary>What the pilot does when another ship attacks.</summary>
    public WFShipPilotStance PilotStance { get; set; } = WFShipPilotStance.Evade;

    /// <summary>Open slots per job id; a job left out is closed.</summary>
    public Dictionary<string, int> Jobs { get; set; } = new();

    /// <summary>Who may use the ship's recovery pads; null when it has none.</summary>
    public WFShipAuthority? PadAccess { get; set; }
}

/// <summary>A person on a command list, by player.</summary>
public sealed class WFShipPresetPerson
{
    /// <summary>The player's user id.</summary>
    public Guid User { get; set; }

    /// <summary>The name shown on the console.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A price rule as a preset stores it; a rule for one person keeps them by player.</summary>
public sealed class WFShipPresetPriceRule
{
    /// <summary>See <see cref="WFShipPriceRule.Name"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>See <see cref="WFShipPriceRule.Audience"/>.</summary>
    public WFShipPriceAudience Audience { get; set; }

    /// <summary>See <see cref="WFShipPriceRule.Company"/>.</summary>
    public string? Company { get; set; }

    /// <summary>See <see cref="WFShipPriceRule.Services"/>.</summary>
    public WFShipServiceKind Services { get; set; } = WFShipServiceKind.All;

    /// <summary>See <see cref="WFShipPriceRule.Multiplier"/>.</summary>
    public float Multiplier { get; set; }

    /// <summary>See <see cref="WFShipPriceRule.Enabled"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The player a rule for one person is for.</summary>
    public Guid? PersonUser { get; set; }

    /// <summary>That person's name, shown on the console.</summary>
    public string? PersonName { get; set; }
}
