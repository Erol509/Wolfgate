using Content.Shared._WF.NpcCrew;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipCommand;

/// <summary>The ship command console's window.</summary>
[Serializable, NetSerializable]
public enum WFShipConsoleUiKey : byte
{
    Key,
}

/// <summary>The owner's on-off switches the console shows.</summary>
[Serializable, NetSerializable]
public enum WFShipConsoleSwitch : byte
{
    AdminsInZones,
    AdminsAtConsole,
    AdminsNeverTargeted,
    AdminsServedFree,
    EventStaffAreAdmins,
    OwnerServedFree,
    DocksWelcome,
    AutoRestaff,
}

/// <summary>Where the crew's answer to someone who may not be there is set: a zone mode, or the barred list.</summary>
[Serializable, NetSerializable]
public enum WFShipConsoleResponseSlot : byte
{
    Staff,
    Locked,
    Sealed,
    Barred,
}

/// <summary>What the Presets tab does with one of the viewer's presets.</summary>
[Serializable, NetSerializable]
public enum WFShipConsolePresetAction : byte
{
    Save,
    Load,
    Delete,
}

/// <summary>How a roster post stands.</summary>
[Serializable, NetSerializable]
public enum WFShipConsoleSlotState : byte
{
    Staffed,
    Empty,
    StoodDown,
    Player,
}

/// <summary>A zone as the console shows it, with how many doors and lockers it holds.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleZone(string Id, string Name, WFShipZoneMode Mode, int Doors, int Lockers);

/// <summary>Someone on a command list, and whether they match by card, by player, or both.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsolePerson(string Name, bool Card, bool Player);

/// <summary>Someone aboard who could be put on a list, with where they stand now.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleAboard(NetEntity Entity, string Name, WFShipAuthority Authority);

/// <summary>A price rule as the console shows it; <see cref="Who"/> names its person or company.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleRule(string Name, WFShipPriceAudience Audience, string Who, WFShipServiceKind Services, float Multiplier, bool Enabled);

/// <summary>A roster post: what stands there, whether it is a counter, how it stands and who holds it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleSlot(string Id, string What, bool Counter, WFShipConsoleSlotState State, string Who);

/// <summary>A player holding one of the ship's jobs.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleHolder(NetEntity Entity, string Name);

/// <summary>A player job: its open slots and who holds it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleJob(string Id, string Name, int Slots, List<WFShipConsoleHolder> Holders);

/// <summary>One of the viewer's presets for this vessel, and when it was saved.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsolePreset(string Name, DateTime Saved);

/// <summary>A recovery pad aboard and who may use it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsolePad(NetEntity Entity, string Name, WFShipAuthority Access);

/// <summary>One service log entry: who, what, the usual price, what they paid and the rule that set it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipConsoleLogEntry(TimeSpan Time, string Customer, WFShipServiceKind Kind, string Service, string What, int BasePrice, int Charged, string? Rule);

/// <summary>
/// The ship command console's state for one viewer. Officers see it all and change nothing; whoever configures the ship
/// changes it; the log is for the owner and admins. Anyone else sees only that they are not cleared.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleState
{
    /// <summary>The console stands on a commanded ship.</summary>
    public bool Commanded;

    /// <summary>The ship's name.</summary>
    public string Ship = string.Empty;

    /// <summary>Where the viewer stands with the ship.</summary>
    public WFShipAuthority Authority = WFShipAuthority.Guest;

    /// <summary>The viewer sees the tabs: they operate the ship.</summary>
    public bool CanView;

    /// <summary>The viewer may change the ship's settings.</summary>
    public bool CanConfigure;

    /// <summary>The viewer sees the service log: the owner or an admin.</summary>
    public bool CanSeeLog;

    /// <summary>The ship's zones.</summary>
    public List<WFShipConsoleZone> Zones = new();

    /// <summary>The crew's answer to someone who may not be where they are, for each case.</summary>
    public Dictionary<WFShipConsoleResponseSlot, WFCrewSecurityResponse> Responses = new();

    /// <summary>Where each of the owner's switches stands.</summary>
    public Dictionary<WFShipConsoleSwitch, bool> Switches = new();

    /// <summary>The officers list.</summary>
    public List<WFShipConsolePerson> Officers = new();

    /// <summary>The staff list.</summary>
    public List<WFShipConsolePerson> Staff = new();

    /// <summary>The barred list.</summary>
    public List<WFShipConsolePerson> Barred = new();

    /// <summary>Everyone aboard who could be put on a list.</summary>
    public List<WFShipConsoleAboard> Aboard = new();

    /// <summary>The ship's price rules.</summary>
    public List<WFShipConsoleRule> Rules = new();

    /// <summary>The ship has a crew roster.</summary>
    public bool HasRoster;

    /// <summary>The roster's posts, by id.</summary>
    public List<WFShipConsoleSlot> Roster = new();

    /// <summary>How long a post stays empty before automatic restaffing fills it, in seconds.</summary>
    public int RestaffDelay;

    /// <summary>The most automatic restaffs in an hour.</summary>
    public int RestaffsPerHour;

    /// <summary>Where the marines stand when nothing is happening.</summary>
    public WFShipMarineOrders MarineOrders;

    /// <summary>When the gunners open fire without being given a target.</summary>
    public WFGunnerPolicy GunnerPolicy;

    /// <summary>What the pilot does when another ship attacks.</summary>
    public WFShipPilotStance PilotStance;

    /// <summary>The radio officer's callsign; empty for the ship's own name.</summary>
    public string Callsign = string.Empty;

    /// <summary>The ship has player jobs.</summary>
    public bool HasJobs;

    /// <summary>The ship's player jobs, by id.</summary>
    public List<WFShipConsoleJob> Jobs = new();

    /// <summary>The vessel the viewer's presets are for; null when the ship has none.</summary>
    public string? Vessel;

    /// <summary>The viewer's presets for the vessel.</summary>
    public List<WFShipConsolePreset> Presets = new();

    /// <summary>The recovery pads aboard.</summary>
    public List<WFShipConsolePad> Pads = new();

    /// <summary>The newest service log entries, newest first.</summary>
    public List<WFShipConsoleLogEntry> Log = new();
}

/// <summary>The console's state, sent to one viewer: what they see depends on who they are.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleStateMessage(WFShipConsoleState state) : BoundUserInterfaceMessage
{
    public readonly WFShipConsoleState State = state;
}

/// <summary>Sent by the window once it is open, and by its refresh button.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleRefreshMessage : BoundUserInterfaceMessage;

/// <summary>Switch one zone, or every zone when <see cref="Zone"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleZoneMessage(string? zone, WFShipZoneMode mode) : BoundUserInterfaceMessage
{
    public readonly string? Zone = zone;
    public readonly WFShipZoneMode Mode = mode;
}

/// <summary>Set the crew's answer for one case.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleResponseMessage(WFShipConsoleResponseSlot slot, WFCrewSecurityResponse response) : BoundUserInterfaceMessage
{
    public readonly WFShipConsoleResponseSlot Slot = slot;
    public readonly WFCrewSecurityResponse Response = response;
}

/// <summary>Turn one of the owner's switches on or off.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleSwitchMessage(WFShipConsoleSwitch which, bool value) : BoundUserInterfaceMessage
{
    public readonly WFShipConsoleSwitch Which = which;
    public readonly bool Value = value;
}

/// <summary>Put someone aboard on a command list, by their card and their player.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleAddPersonMessage(NetEntity person, WFShipCommandList list) : BoundUserInterfaceMessage
{
    public readonly NetEntity Person = person;
    public readonly WFShipCommandList List = list;
}

/// <summary>Change the crew's standing orders; whatever is null stays as it is.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleOrdersMessage(WFShipMarineOrders? marines, WFGunnerPolicy? gunners, WFShipPilotStance? pilot, string? callsign) : BoundUserInterfaceMessage
{
    public readonly WFShipMarineOrders? Marines = marines;
    public readonly WFGunnerPolicy? Gunners = gunners;
    public readonly WFShipPilotStance? Pilot = pilot;
    public readonly string? Callsign = callsign;
}

/// <summary>Take the person at <see cref="Index"/> off a command list, if that is still who is named there.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleRemovePersonMessage(WFShipCommandList list, int index, string name) : BoundUserInterfaceMessage
{
    public readonly WFShipCommandList List = list;
    public readonly int Index = index;
    public readonly string Name = name;
}

/// <summary>Add a price rule, or replace the one with the same name.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleSetRuleMessage(string name, WFShipPriceAudience audience, NetEntity? person, string? company,
    WFShipServiceKind services, float multiplier) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
    public readonly WFShipPriceAudience Audience = audience;
    public readonly NetEntity? Person = person;
    public readonly string? Company = company;
    public readonly WFShipServiceKind Services = services;
    public readonly float Multiplier = multiplier;
}

/// <summary>Switch a price rule on or off.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleToggleRuleMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

/// <summary>Remove a price rule.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleRemoveRuleMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

/// <summary>Staff one roster post, or every empty one when <see cref="Slot"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleStaffMessage(string? slot) : BoundUserInterfaceMessage
{
    public readonly string? Slot = slot;
}

/// <summary>Stand a roster post down, or up again.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleStandDownMessage(string slot, bool down) : BoundUserInterfaceMessage
{
    public readonly string Slot = slot;
    public readonly bool Down = down;
}

/// <summary>Automatic restaffing's delay, in seconds, and its hourly cap.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleRestaffMessage(int delay, int perHour) : BoundUserInterfaceMessage
{
    public readonly int Delay = delay;
    public readonly int PerHour = perHour;
}

/// <summary>Set how many slots a player job has open.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleJobSlotsMessage(string job, int slots) : BoundUserInterfaceMessage
{
    public readonly string Job = job;
    public readonly int Slots = slots;
}

/// <summary>Release a player from their job, handing the post back to the roster.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsoleReleaseJobMessage(NetEntity holder) : BoundUserInterfaceMessage
{
    public readonly NetEntity Holder = holder;
}

/// <summary>Save, load or delete one of the viewer's presets by name.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsolePresetMessage(WFShipConsolePresetAction action, string name) : BoundUserInterfaceMessage
{
    public readonly WFShipConsolePresetAction Action = action;
    public readonly string Name = name;
}

/// <summary>Change who may use one of the recovery pads aboard.</summary>
[Serializable, NetSerializable]
public sealed class WFShipConsolePadAccessMessage(NetEntity pad, WFShipAuthority access) : BoundUserInterfaceMessage
{
    public readonly NetEntity Pad = pad;
    public readonly WFShipAuthority Access = access;
}
