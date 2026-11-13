using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Server._NF.Station.Components;
using Content.Server._WF.Administration.Systems;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Database;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Personal presets for commanded ships. The ship spawns fresh each round; a preset carries its settings
/// (<see cref="WFShipPresetData"/>) between rounds. Presets are kept per player, per vessel design and by name, and
/// each player sees and applies only their own. Admin Spawn Vessel applies the owner's or the spawning admin's
/// "default" preset, or the one it names.
/// </summary>
public sealed class WFShipPresetSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private WFShipBehaviourSystem _behaviour = default!;
    [Dependency] private WFShipCommandSystem _command = default!;
    [Dependency] private WFShipJobsSystem _jobs = default!;
    [Dependency] private WFShipRosterSystem _roster = default!;

    /// <summary>The preset Spawn Vessel applies when it names none.</summary>
    public const string DefaultName = "default";

    public const int MaxNameLength = 32;

    private static readonly Regex NamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFAdminVesselSpawnedEvent>(OnVesselSpawned);
    }

    /// <summary>Whether a preset name is usable: letters, digits, dashes and underscores, up to <see cref="MaxNameLength"/>.</summary>
    public static bool ValidName(string name)
    {
        return name is { Length: > 0 and <= MaxNameLength } && NamePattern.IsMatch(name);
    }

    /// <summary>A preset as the database stores it.</summary>
    public static string Serialize(WFShipPresetData data)
    {
        return JsonSerializer.Serialize(data, Json);
    }

    /// <summary>A preset from the database; throws <see cref="JsonException"/> when it can't be read.</summary>
    public static WFShipPresetData? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<WFShipPresetData>(json, Json);
    }

    /// <summary>The vessel design a commanded ship was spawned from, which its presets are kept under.</summary>
    public string? VesselOf(EntityUid grid)
    {
        return _station.GetOwningStation(grid) is { } station
               && TryComp<ExtraShuttleInformationComponent>(station, out var info)
               && info.Vessel is { } vessel
            ? vessel.Id
            : null;
    }

    /// <summary>Reads a ship's settings into a preset. People added by card alone are left out: they are this round's.</summary>
    public WFShipPresetData Capture(EntityUid grid)
    {
        var data = new WFShipPresetData();
        if (TryComp<WFShipZonesComponent>(grid, out var zones))
        {
            foreach (var zone in zones.Zones)
                data.Zones[zone.Id] = zone.Mode;
        }

        if (TryComp<WFShipCommandComponent>(grid, out var command))
        {
            data.Officers = People(command.Officers);
            data.Staff = People(command.Staff);
            data.Barred = People(command.Barred);
            data.AdminsInZones = command.AdminsInZones;
            data.AdminsAtConsole = command.AdminsAtConsole;
            data.AdminsNeverTargeted = command.AdminsNeverTargeted;
            data.AdminsServedFree = command.AdminsServedFree;
            data.EventStaffAreAdmins = command.EventStaffAreAdmins;
            data.OwnerServedFree = command.OwnerServedFree;
            data.DocksWelcome = command.DocksWelcome;
            data.StaffZoneResponse = command.StaffZoneResponse;
            data.LockedZoneResponse = command.LockedZoneResponse;
            data.SealedZoneResponse = command.SealedZoneResponse;
            data.BarredResponse = command.BarredResponse;
            data.PriceRules = command.PriceRules
                .Where(r => r.Audience != WFShipPriceAudience.Person || r.Person?.User != null)
                .Select(r => new WFShipPresetPriceRule
                {
                    Name = r.Name,
                    Audience = r.Audience,
                    Company = r.Company,
                    Services = r.Services,
                    Multiplier = r.Multiplier,
                    Enabled = r.Enabled,
                    PersonUser = r.Person?.User?.UserId,
                    PersonName = r.Person?.Name,
                })
                .ToList();
        }

        if (TryComp<WFShipRosterComponent>(grid, out var roster))
        {
            data.AutoRestaff = roster.AutoRestaff;
            data.RestaffDelaySeconds = (int) roster.RestaffDelay.TotalSeconds;
            data.RestaffsPerHour = roster.RestaffsPerHour;
            data.StoodDown = roster.Slots.Values.Where(s => s.StoodDown).Select(s => s.Id).OrderBy(id => id).ToList();
            data.Callsign = roster.Callsign;
            data.MarineOrders = roster.MarineOrders;
            data.GunnerPolicy = roster.GunnerPolicy;
            data.PilotStance = roster.PilotStance;
        }

        if (TryComp<WFShipJobsComponent>(grid, out var jobs))
        {
            foreach (var (job, open) in _jobs.GetSlots((grid, jobs)))
            {
                if (open > 0)
                    data.Jobs[job.Id] = open;
            }
        }

        var pads = EntityQueryEnumerator<WFRecoveryPadComponent, TransformComponent>();
        while (pads.MoveNext(out _, out var pad, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            data.PadAccess = pad.Access;
            break;
        }

        return data;
    }

    private static List<WFShipPresetPerson> People(List<WFShipCommandPerson> people)
    {
        return people
            .Where(p => p.User != null)
            .Select(p => new WFShipPresetPerson { User = p.User!.Value.UserId, Name = p.Name })
            .ToList();
    }

    /// <summary>
    /// Sets a ship to a preset: zones switched, lists and rules replaced, posts stood down or up, job slots set.
    /// Listed players are matched to whatever card they wear this round.
    /// </summary>
    public void Apply(EntityUid grid, WFShipPresetData data, EntityUid? actor, string name)
    {
        if (TryComp<WFShipZonesComponent>(grid, out var zones))
        {
            foreach (var (zone, mode) in data.Zones)
                _command.SetZoneMode((grid, zones), zone, mode, actor);
        }

        if (TryComp<WFShipCommandComponent>(grid, out var command))
        {
            command.Officers = Merged(grid, WFShipCommandList.Officers, command.Officers, data.Officers);
            command.Staff = Merged(grid, WFShipCommandList.Staff, command.Staff, data.Staff);
            command.Barred = Merged(grid, WFShipCommandList.Barred, command.Barred, data.Barred);
            command.AdminsInZones = data.AdminsInZones;
            command.AdminsAtConsole = data.AdminsAtConsole;
            command.AdminsNeverTargeted = data.AdminsNeverTargeted;
            command.AdminsServedFree = data.AdminsServedFree;
            command.EventStaffAreAdmins = data.EventStaffAreAdmins;
            command.OwnerServedFree = data.OwnerServedFree;
            command.DocksWelcome = data.DocksWelcome;
            command.StaffZoneResponse = data.StaffZoneResponse;
            command.LockedZoneResponse = data.LockedZoneResponse;
            command.SealedZoneResponse = data.SealedZoneResponse;
            command.BarredResponse = data.BarredResponse;
            command.PriceRules = data.PriceRules.Select(r => new WFShipPriceRule
            {
                Name = r.Name,
                Audience = r.Audience,
                Company = r.Company,
                Services = r.Services,
                Multiplier = r.Multiplier,
                Enabled = r.Enabled,
                Person = r.PersonUser is { } user
                    ? new WFShipCommandPerson { User = new NetUserId(user), Name = r.PersonName ?? string.Empty }
                    : null,
            }).ToList();
            Dirty(grid, command);
            _command.RefreshZoneKeys(grid);
        }

        if (TryComp<WFShipRosterComponent>(grid, out var roster))
        {
            var ship = (grid, roster);
            roster.AutoRestaff = data.AutoRestaff;
            roster.RestaffDelay = TimeSpan.FromSeconds(Math.Max(0, data.RestaffDelaySeconds));
            roster.RestaffsPerHour = Math.Max(0, data.RestaffsPerHour);
            roster.Callsign = WFShipBehaviourSystem.CleanCallsign(data.Callsign);
            roster.MarineOrders = data.MarineOrders;
            roster.GunnerPolicy = data.GunnerPolicy;
            roster.PilotStance = data.PilotStance;
            foreach (var slot in roster.Slots.Keys.ToList())
                _roster.SetStoodDown(ship, slot, data.StoodDown.Contains(slot), actor);
            _behaviour.ApplyAll(ship);
        }

        if (TryComp<WFShipJobsComponent>(grid, out var jobs))
        {
            foreach (var job in jobs.Jobs.Keys)
                _jobs.SetSlots((grid, jobs), job, Math.Clamp(data.Jobs.GetValueOrDefault(job.Id), 0, WFShipConsoleSystem.MaxJobSlots), actor);
        }

        if (data.PadAccess is { } access && access is WFShipAuthority.Staff or WFShipAuthority.Officer or WFShipAuthority.Admin)
        {
            var pads = EntityQueryEnumerator<WFRecoveryPadComponent, TransformComponent>();
            while (pads.MoveNext(out _, out var pad, out var xform))
            {
                if (xform.GridUid == grid)
                    pad.Access = access;
            }
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} applied ship preset {name} to {ToPrettyString(grid):grid}");
    }

    private static List<WFShipCommandPerson> Persons(List<WFShipPresetPerson> people)
    {
        return people.Select(p => new WFShipCommandPerson { User = new NetUserId(p.User), Name = p.Name }).ToList();
    }

    /// <summary>A preset's list, plus who it can't name: this round's cards, and players whose job gives them this standing.</summary>
    private List<WFShipCommandPerson> Merged(EntityUid grid, WFShipCommandList list, List<WFShipCommandPerson> current, List<WFShipPresetPerson> saved)
    {
        var holders = new HashSet<NetUserId>();
        var query = EntityQueryEnumerator<WFShipJobHolderComponent>();
        while (query.MoveNext(out var holder))
        {
            if (holder.Grid == grid && holder.Standing == list)
                holders.Add(holder.User);
        }

        var merged = Persons(saved);
        foreach (var person in current)
        {
            if (person.User is not { } user || holders.Contains(user) && !merged.Exists(p => p.User == user))
                merged.Add(person);
        }

        return merged;
    }

    /// <summary>Saves a ship's settings as one of a player's presets for its vessel. False when the ship has no vessel design.</summary>
    public async Task<bool> Save(EntityUid grid, NetUserId user, string name)
    {
        if (!ValidName(name) || VesselOf(grid) is not { } vessel)
            return false;

        var json = JsonSerializer.Serialize(Capture(grid), Json);
        await _db.SaveShipCommandPresetAsync(user.UserId, vessel, name, json);
        return true;
    }

    /// <summary>One of a player's presets for a vessel design, or null.</summary>
    public async Task<WFShipPresetData?> Fetch(NetUserId user, string vessel, string name)
    {
        var json = await _db.GetShipCommandPresetAsync(user.UserId, vessel, name);
        if (json == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<WFShipPresetData>(json, Json);
        }
        catch (JsonException e)
        {
            Log.Error($"Unreadable ship preset {name} of {user} for {vessel}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies the first of these players' presets of a name that exists, in order. Returns whose was applied, or null.
    /// </summary>
    public async Task<NetUserId?> LoadFirst(EntityUid grid, IEnumerable<NetUserId> users, string name, EntityUid? actor)
    {
        if (VesselOf(grid) is not { } vessel)
            return null;

        foreach (var user in users)
        {
            var data = await Fetch(user, vessel, name);
            if (data == null || TerminatingOrDeleted(grid))
                continue;

            Apply(grid, data, actor, name);
            return user;
        }

        return null;
    }

    /// <summary>
    /// Spawn Vessel: a named preset is the spawning admin's; without one, the owner's default applies, else the admin's.
    /// </summary>
    private async void OnVesselSpawned(WFAdminVesselSpawnedEvent ev)
    {
        if (!HasComp<WFShipCommandComponent>(ev.Grid))
            return;

        var users = new List<NetUserId>();
        if (ev.Preset == null && ev.Owner is { } owner)
            users.Add(owner.UserId);
        if (ev.Spawner is { } spawner)
            users.Add(spawner.UserId);

        var name = ev.Preset ?? DefaultName;
        try
        {
            var applied = await LoadFirst(ev.Grid, users, name, ev.Spawner?.AttachedEntity);
            if (ev.Spawner is not { } admin)
                return;

            if (applied != null)
                _chat.DispatchServerMessage(admin, Loc.GetString("wf-shipcommand-preset-applied", ("name", name)));
            else if (ev.Preset != null)
                _chat.DispatchServerMessage(admin, Loc.GetString("wf-shipcommand-preset-missing", ("name", name)));
        }
        catch (Exception e)
        {
            Log.Error($"Could not apply ship preset {name} to {ToPrettyString(ev.Grid)}: {e}");
        }
    }
}
