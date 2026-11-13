using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's command console (<see cref="WFShipConsoleComponent"/>): the bridge front end for everything the
/// <c>wf_ship*</c> commands do. Zones and the crew's answers, the command lists and the admins' powers, price rules,
/// the roster, the crew's standing orders, player jobs, the viewer's own presets, the recovery pads' access, and the service log. Officers see it
/// and change nothing; whoever configures the ship (<see cref="WFShipCommandSystem.CanConfigure"/>) changes it; the
/// log is for the owner and admins. Every change goes through the same system call the commands use, so it is logged
/// the same way.
/// </summary>
public sealed class WFShipConsoleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFRecoveryPadSystem _pads = default!;
    [Dependency] private WFShipAccessSystem _access = default!;
    [Dependency] private WFShipBehaviourSystem _behaviour = default!;
    [Dependency] private WFShipCommandSystem _command = default!;
    [Dependency] private WFShipJobsSystem _jobs = default!;
    [Dependency] private WFShipPresetSystem _presets = default!;
    [Dependency] private WFShipRosterSystem _roster = default!;
    [Dependency] private WFShipServiceSystem _service = default!;

    /// <summary>The most service log entries the console shows, newest first.</summary>
    public const int LogLines = 200;

    /// <summary>The most people aboard the people tab lists.</summary>
    public const int AboardLimit = 60;

    /// <summary>The most open slots the console sets for one job.</summary>
    public const int MaxJobSlots = 10;

    /// <summary>The longest automatic restaffing delay, in seconds.</summary>
    public const int MaxRestaffDelay = 3600;

    /// <summary>The highest hourly cap on automatic restaffs.</summary>
    public const int MaxRestaffsPerHour = 60;

    /// <summary>The longest price rule name, and company id.</summary>
    public const int MaxRuleName = 32;

    /// <summary>The most price rules one ship keeps.</summary>
    public const int MaxRules = 64;

    /// <summary>The most presets one player keeps for one vessel.</summary>
    public const int MaxPresets = 24;

    private static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshMessageGap = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PresetReadGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PresetActionGap = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ChangedGap = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextRefresh;
    private TimeSpan _nextChangedSend;

    /// <summary>Consoles whose viewers are owed a new state, sent together so a stream of messages can't force one each.</summary>
    private readonly HashSet<EntityUid> _changed = new();

    /// <summary>When each viewer's refresh button may next rebuild their state.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _refreshAt = new();

    /// <summary>When each player may next save, load or delete a preset.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _presetAction = new();

    /// <summary>When each viewer's refresh may next read their presets.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _presetRead = new();

    /// <summary>Each player's presets for each vessel, as last read from the database, kept while they have a console open.</summary>
    private readonly Dictionary<(NetUserId, string), List<WFShipConsolePreset>> _presetLists = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleZoneMessage>(OnZone);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleResponseMessage>(OnResponse);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleSwitchMessage>(OnSwitch);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleAddPersonMessage>(OnAddPerson);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleRemovePersonMessage>(OnRemovePerson);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleSetRuleMessage>(OnSetRule);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleToggleRuleMessage>(OnToggleRule);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleRemoveRuleMessage>(OnRemoveRule);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleStaffMessage>(OnStaff);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleStandDownMessage>(OnStandDown);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleRestaffMessage>(OnRestaff);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleOrdersMessage>(OnOrders);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleJobSlotsMessage>(OnJobSlots);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsoleReleaseJobMessage>(OnReleaseJob);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsolePresetMessage>(OnPreset);
        SubscribeLocalEvent<WFShipConsoleComponent, WFShipConsolePadAccessMessage>(OnPadAccess);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (_changed.Count > 0 && now >= _nextChangedSend)
        {
            _nextChangedSend = now + ChangedGap;
            foreach (var console in _changed)
            {
                if (!TerminatingOrDeleted(console))
                    SendAll(console);
            }

            _changed.Clear();
        }

        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshGap;
        var viewers = new HashSet<NetUserId>();
        var consoles = EntityQueryEnumerator<WFShipConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            SendAll(uid);
            foreach (var actor in _ui.GetActors(uid, WFShipConsoleUiKey.Key))
            {
                if (UserOf(actor) is { } user)
                    viewers.Add(user);
            }
        }

        // Presets are read again when a window opens, so a player who closed theirs or left needn't keep them.
        foreach (var key in _presetLists.Keys.Where(k => !viewers.Contains(k.Item1)).ToList())
            _presetLists.Remove(key);
    }

    #region State

    /// <summary>The commanded ship the console stands on.</summary>
    public bool TryGetShip(EntityUid console, out Entity<WFShipCommandComponent> ship)
    {
        ship = default;
        if (Transform(console).GridUid is not { } grid || !TryComp<WFShipCommandComponent>(grid, out var command))
            return false;

        ship = (grid, command);
        return true;
    }

    /// <summary>The console's state as one viewer sees it.</summary>
    public WFShipConsoleState State(EntityUid console, EntityUid viewer)
    {
        var state = new WFShipConsoleState();
        if (!TryGetShip(console, out var ship))
            return state;

        var comp = ship.Comp;
        state.Commanded = true;
        state.Ship = Name(ship);
        state.Authority = _command.GetAuthority(viewer, ship);
        state.CanView = _command.CanOperate(viewer, ship);
        state.CanConfigure = _command.CanConfigure(viewer, ship);
        state.CanSeeLog = state.Authority >= WFShipAuthority.Admin;
        if (!state.CanView)
            return state;

        if (TryComp<WFShipZonesComponent>(ship, out var zones))
        {
            var counts = _command.ZoneCounts(ship);
            foreach (var zone in zones.Zones)
            {
                var (doors, lockers) = counts.GetValueOrDefault(zone.Id);
                state.Zones.Add(new WFShipConsoleZone(zone.Id, Loc.GetString(zone.Name), zone.Mode, doors, lockers));
            }
        }

        state.Responses[WFShipConsoleResponseSlot.Staff] = comp.StaffZoneResponse;
        state.Responses[WFShipConsoleResponseSlot.Locked] = comp.LockedZoneResponse;
        state.Responses[WFShipConsoleResponseSlot.Sealed] = comp.SealedZoneResponse;
        state.Responses[WFShipConsoleResponseSlot.Barred] = comp.BarredResponse;
        foreach (var which in Enum.GetValues<WFShipConsoleSwitch>())
            state.Switches[which] = GetSwitch(ship, which);

        state.Officers = comp.Officers.ConvertAll(Person);
        state.Staff = comp.Staff.ConvertAll(Person);
        state.Barred = comp.Barred.ConvertAll(Person);
        state.Aboard = Aboard(ship);

        foreach (var rule in comp.PriceRules)
        {
            var who = rule.Audience switch
            {
                WFShipPriceAudience.Person => rule.Person?.Name ?? string.Empty,
                WFShipPriceAudience.Company => rule.Company ?? string.Empty,
                _ => string.Empty,
            };
            state.Rules.Add(new WFShipConsoleRule(rule.Name, rule.Audience, who, rule.Services, rule.Multiplier, rule.Enabled));
        }

        if (TryComp<WFShipRosterComponent>(ship, out var roster))
        {
            state.HasRoster = true;
            state.RestaffDelay = (int) roster.RestaffDelay.TotalSeconds;
            state.RestaffsPerHour = roster.RestaffsPerHour;
            state.MarineOrders = roster.MarineOrders;
            state.GunnerPolicy = roster.GunnerPolicy;
            state.PilotStance = roster.PilotStance;
            state.Callsign = roster.Callsign;
            foreach (var slot in roster.Slots.Values.OrderBy(s => s.Id))
            {
                var held = slot.Holder is { } holder && Exists(holder);
                var slotState = held ? WFShipConsoleSlotState.Player
                    : slot.StoodDown ? WFShipConsoleSlotState.StoodDown
                    : _roster.Held(slot) ? WFShipConsoleSlotState.Staffed
                    : WFShipConsoleSlotState.Empty;
                var who = (held ? slot.Holder : slot.Occupant) is { } mob && Exists(mob) ? Name(mob) : string.Empty;
                state.Roster.Add(new WFShipConsoleSlot(slot.Id, What(slot), slot.Kind == WFShipRosterKind.Counter, slotState, who));
            }
        }

        if (TryComp<WFShipJobsComponent>(ship, out var jobs))
        {
            state.HasJobs = true;
            var slots = _jobs.GetSlots((ship, jobs));
            var holders = _jobs.Holders(ship);
            foreach (var job in jobs.Jobs.Keys.OrderBy(j => j.Id))
            {
                var name = _protos.TryIndex(job, out var proto) ? proto.LocalizedName : job.Id;
                var held = holders.Where(h => h.Comp.Job == job)
                    .Select(h => new WFShipConsoleHolder(GetNetEntity(h.Owner), Name(h.Owner)))
                    .ToList();
                state.Jobs.Add(new WFShipConsoleJob(job.Id, name, slots.GetValueOrDefault(job), held));
            }
        }

        state.Vessel = _presets.VesselOf(ship);
        if (state.Vessel is { } vessel && UserOf(viewer) is { } user && _presetLists.TryGetValue((user, vessel), out var presets))
            state.Presets = presets;

        var pads = EntityQueryEnumerator<WFRecoveryPadComponent, TransformComponent>();
        while (pads.MoveNext(out var uid, out var pad, out var xform))
        {
            if (xform.GridUid == ship.Owner)
                state.Pads.Add(new WFShipConsolePad(GetNetEntity(uid), Name(uid), pad.Access));
        }

        if (state.CanSeeLog && TryComp<WFShipServiceLedgerComponent>(ship, out var ledger))
        {
            for (var i = ledger.Entries.Count - 1; i >= 0 && state.Log.Count < LogLines; i--)
            {
                var e = ledger.Entries[i];
                state.Log.Add(new WFShipConsoleLogEntry(e.Time, e.Customer, e.Kind, e.Service, e.What, e.BasePrice, e.Charged, e.Rule));
            }
        }

        return state;
    }

    private static WFShipConsolePerson Person(WFShipCommandPerson person)
    {
        return new WFShipConsolePerson(person.Name, person.Key != null, person.User != null);
    }

    private string What(WFShipRosterSlot slot)
    {
        if (slot.Kind == WFShipRosterKind.Counter)
            return slot.Trader is { } trader && _protos.TryIndex(trader, out var proto) ? proto.Name : slot.What;

        return _protos.TryIndex(slot.Role, out var role) ? Loc.GetString(role.Title) : slot.What;
    }

    /// <summary>Everyone aboard with a mind (players and the characters they left), by name, with their standing.</summary>
    public List<WFShipConsoleAboard> Aboard(Entity<WFShipCommandComponent> ship)
    {
        var result = new List<WFShipConsoleAboard>();
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid != ship.Owner || HasComp<GhostComponent>(uid) || !_mind.TryGetMind(uid, out _, out _))
                continue;

            result.Add(new WFShipConsoleAboard(GetNetEntity(uid), Name(uid), _command.GetAuthority(uid, ship)));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        if (result.Count > AboardLimit)
            result.RemoveRange(AboardLimit, result.Count - AboardLimit);
        return result;
    }

    private NetUserId? UserOf(EntityUid viewer)
    {
        return _player.TryGetSessionByEntity(viewer, out var session) ? session.UserId : null;
    }

    private void SendState(EntityUid console, EntityUid viewer)
    {
        _ui.ServerSendUiMessage(console, WFShipConsoleUiKey.Key, new WFShipConsoleStateMessage(State(console, viewer)), viewer);
    }

    /// <summary>Sends every viewer of the console their state within a quarter second.</summary>
    private void MarkChanged(EntityUid console)
    {
        _changed.Add(console);
    }

    /// <summary>Sends every viewer of the console their state.</summary>
    private void SendAll(EntityUid console)
    {
        foreach (var actor in _ui.GetActors(console, WFShipConsoleUiKey.Key).ToList())
            SendState(console, actor);
    }

    private void OnOpened(Entity<WFShipConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        SendState(console, args.Actor);
        _ = LoadPresets(console, args.Actor);
    }

    private void OnRefresh(Entity<WFShipConsoleComponent> console, ref WFShipConsoleRefreshMessage args)
    {
        // A held-down refresh shouldn't rebuild the whole state every tick.
        var now = _timing.CurTime;
        if (now < _refreshAt.GetValueOrDefault(args.Actor))
            return;

        if (_refreshAt.Count > 64)
            _refreshAt.Clear();
        _refreshAt[args.Actor] = now + RefreshMessageGap;
        SendState(console, args.Actor);
        if (now < _presetRead.GetValueOrDefault(args.Actor))
            return;

        // A held-down refresh shouldn't turn into a stream of database reads.
        _presetRead[args.Actor] = now + PresetReadGap;
        if (_presetRead.Count > 64)
            _presetRead.Clear();
        _ = LoadPresets(console, args.Actor);
    }

    #endregion

    #region Changes

    /// <summary>
    /// The ship, when the actor may change its settings. Otherwise they are told they aren't cleared and the console
    /// changes nothing.
    /// </summary>
    private bool Configuring(EntityUid console, EntityUid actor, out Entity<WFShipCommandComponent> ship)
    {
        if (TryGetShip(console, out ship) && _command.CanConfigure(actor, ship))
            return true;

        _popup.PopupEntity(Loc.GetString("wf-ship-console-not-cleared"), console, actor, PopupType.MediumCaution);
        return false;
    }

    private void OnZone(Entity<WFShipConsoleComponent> console, ref WFShipConsoleZoneMessage args)
    {
        if (!Enum.IsDefined(args.Mode) || !Configuring(console, args.Actor, out var ship)
            || !TryComp<WFShipZonesComponent>(ship, out var zones))
            return;

        var ids = args.Zone is { } one ? new List<string> { one } : zones.Zones.Select(z => z.Id).ToList();
        foreach (var id in ids)
            _command.SetZoneMode((ship, zones), id, args.Mode, args.Actor);
        MarkChanged(console);
    }

    private void OnResponse(Entity<WFShipConsoleComponent> console, ref WFShipConsoleResponseMessage args)
    {
        if (!Enum.IsDefined(args.Slot) || !Enum.IsDefined(args.Response) || !Configuring(console, args.Actor, out var ship))
            return;

        var comp = ship.Comp;
        switch (args.Slot)
        {
            case WFShipConsoleResponseSlot.Staff:
                comp.StaffZoneResponse = args.Response;
                break;
            case WFShipConsoleResponseSlot.Locked:
                comp.LockedZoneResponse = args.Response;
                break;
            case WFShipConsoleResponseSlot.Sealed:
                comp.SealedZoneResponse = args.Response;
                break;
            case WFShipConsoleResponseSlot.Barred:
                comp.BarredResponse = args.Response;
                break;
        }

        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):actor} set the crew's {args.Slot} response on {ToPrettyString(ship.Owner):grid} to {args.Response}");
        MarkChanged(console);
    }

    /// <summary>Where one of the owner's switches stands; automatic restaffing is off on a ship without a roster.</summary>
    public bool GetSwitch(Entity<WFShipCommandComponent> ship, WFShipConsoleSwitch which)
    {
        var comp = ship.Comp;
        return which switch
        {
            WFShipConsoleSwitch.AdminsInZones => comp.AdminsInZones,
            WFShipConsoleSwitch.AdminsAtConsole => comp.AdminsAtConsole,
            WFShipConsoleSwitch.AdminsNeverTargeted => comp.AdminsNeverTargeted,
            WFShipConsoleSwitch.AdminsServedFree => comp.AdminsServedFree,
            WFShipConsoleSwitch.EventStaffAreAdmins => comp.EventStaffAreAdmins,
            WFShipConsoleSwitch.OwnerServedFree => comp.OwnerServedFree,
            WFShipConsoleSwitch.DocksWelcome => comp.DocksWelcome,
            WFShipConsoleSwitch.AutoRestaff => CompOrNull<WFShipRosterComponent>(ship)?.AutoRestaff ?? false,
            _ => false,
        };
    }

    /// <summary>Sets one of the owner's switches. Admin-logged. False when the actor may not, or nothing changed.</summary>
    public bool SetSwitch(EntityUid console, EntityUid actor, WFShipConsoleSwitch which, bool value)
    {
        if (!Enum.IsDefined(which) || !Configuring(console, actor, out var ship) || GetSwitch(ship, which) == value)
            return false;

        var comp = ship.Comp;
        switch (which)
        {
            case WFShipConsoleSwitch.AdminsInZones:
                _command.SetAdminsInZones(ship, value);
                break;
            case WFShipConsoleSwitch.AdminsAtConsole:
                comp.AdminsAtConsole = value;
                break;
            case WFShipConsoleSwitch.AdminsNeverTargeted:
                comp.AdminsNeverTargeted = value;
                break;
            case WFShipConsoleSwitch.AdminsServedFree:
                comp.AdminsServedFree = value;
                break;
            case WFShipConsoleSwitch.EventStaffAreAdmins:
                comp.EventStaffAreAdmins = value;
                _command.RefreshZoneKeys(ship);
                break;
            case WFShipConsoleSwitch.OwnerServedFree:
                comp.OwnerServedFree = value;
                break;
            case WFShipConsoleSwitch.DocksWelcome:
                comp.DocksWelcome = value;
                break;
            case WFShipConsoleSwitch.AutoRestaff:
                if (!TryComp<WFShipRosterComponent>(ship, out var roster))
                    return false;
                _roster.SetAutoRestaff((ship, roster), value, actor);
                break;
        }

        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} switched {which} {(value ? "on" : "off")} on {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    private void OnSwitch(Entity<WFShipConsoleComponent> console, ref WFShipConsoleSwitchMessage args)
    {
        SetSwitch(console, args.Actor, args.Which, args.Value);
        MarkChanged(console);
    }

    /// <summary>
    /// Puts someone aboard on a command list by the card they carry and their player, taking them off the other lists.
    /// False when the actor may not, or the person is not aboard.
    /// </summary>
    public bool AddPerson(EntityUid console, EntityUid actor, EntityUid person, WFShipCommandList list)
    {
        if (!Enum.IsDefined(list) || !Configuring(console, actor, out var ship)
            || Transform(person).GridUid != ship.Owner || !_mind.TryGetMind(person, out _, out var mind))
            return false;

        WFShipAccessKey? key = _access.TryGetCard(person, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        var user = UserOf(person) ?? mind.UserId;
        return _command.AddPerson(ship, list, key, user, Name(person));
    }

    private void OnAddPerson(Entity<WFShipConsoleComponent> console, ref WFShipConsoleAddPersonMessage args)
    {
        if (GetEntity(args.Person) is { Valid: true } person)
            AddPerson(console, args.Actor, person, args.List);
        MarkChanged(console);
    }

    private void OnRemovePerson(Entity<WFShipConsoleComponent> console, ref WFShipConsoleRemovePersonMessage args)
    {
        if (!Enum.IsDefined(args.List) || !Configuring(console, args.Actor, out var ship))
            return;

        var people = args.List switch
        {
            WFShipCommandList.Officers => ship.Comp.Officers,
            WFShipCommandList.Staff => ship.Comp.Staff,
            _ => ship.Comp.Barred,
        };

        if (args.Index >= 0 && args.Index < people.Count && people[args.Index].Name == args.Name)
        {
            var person = people[args.Index];
            _command.RemovePerson(ship, args.List, person.Key, person.User);
        }

        MarkChanged(console);
    }

    /// <summary>
    /// Adds a price rule or replaces the one of the same name, after checking it: a name (not "owner" or "admins", which
    /// name the free-service switches), some services, a multiplier from 0 to 10, and the person or company it needs.
    /// </summary>
    public bool SetRule(EntityUid console, EntityUid actor, WFShipConsoleSetRuleMessage rule)
    {
        var name = rule.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxRuleName || name is "owner" or "admins"
            || !Enum.IsDefined(rule.Audience) || rule.Services == WFShipServiceKind.None
            || (rule.Services & ~WFShipServiceKind.All) != 0
            || !float.IsFinite(rule.Multiplier) || rule.Multiplier is < 0 or > 10)
            return false;

        if (!Configuring(console, actor, out var ship)
            || ship.Comp.PriceRules.Count >= MaxRules && !ship.Comp.PriceRules.Exists(r => r.Name == name))
            return false;

        WFShipCommandPerson? person = null;
        string? company = null;
        switch (rule.Audience)
        {
            case WFShipPriceAudience.Person:
                if (rule.Person is not { } net || GetEntity(net) is not { Valid: true } target
                    || Transform(target).GridUid != ship.Owner || !_mind.TryGetMind(target, out _, out var mind))
                    return false;
                WFShipAccessKey? key = _access.TryGetCard(target, out var card) && _access.TryGetKey(card, out var found) ? found : null;
                person = new WFShipCommandPerson { Key = key, User = UserOf(target) ?? mind.UserId, Name = Name(target) };
                if (person.Key == null && person.User == null)
                    return false;
                break;
            case WFShipPriceAudience.Company:
                company = rule.Company?.Trim();
                if (string.IsNullOrEmpty(company) || company.Length > MaxRuleName)
                    return false;
                break;
        }

        _service.SetRule(ship, new WFShipPriceRule
        {
            Name = name,
            Audience = rule.Audience,
            Person = person,
            Company = company,
            Services = rule.Services,
            Multiplier = rule.Multiplier,
        }, actor);
        return true;
    }

    private void OnSetRule(Entity<WFShipConsoleComponent> console, ref WFShipConsoleSetRuleMessage args)
    {
        if (!SetRule(console, args.Actor, args))
            _popup.PopupEntity(Loc.GetString("wf-ship-console-bad-rule"), console, args.Actor, PopupType.MediumCaution);
        MarkChanged(console);
    }

    private void OnToggleRule(Entity<WFShipConsoleComponent> console, ref WFShipConsoleToggleRuleMessage args)
    {
        var name = args.Name;
        if (Configuring(console, args.Actor, out var ship) && ship.Comp.PriceRules.Exists(r => r.Name == name))
            _service.ToggleRule(ship, name, args.Actor, out _);
        MarkChanged(console);
    }

    private void OnRemoveRule(Entity<WFShipConsoleComponent> console, ref WFShipConsoleRemoveRuleMessage args)
    {
        if (Configuring(console, args.Actor, out var ship))
            _service.RemoveRule(ship, args.Name, args.Actor);
        MarkChanged(console);
    }

    private bool Roster(EntityUid console, EntityUid actor, out Entity<WFShipRosterComponent> roster)
    {
        roster = default;
        if (!Configuring(console, actor, out var ship) || !TryComp<WFShipRosterComponent>(ship, out var comp))
            return false;

        roster = (ship, comp);
        return true;
    }

    private void OnStaff(Entity<WFShipConsoleComponent> console, ref WFShipConsoleStaffMessage args)
    {
        if (Roster(console, args.Actor, out var roster) && (args.Slot == null || roster.Comp.Slots.ContainsKey(args.Slot)))
            _roster.Staff(roster, args.Slot, args.Actor);
        MarkChanged(console);
    }

    private void OnStandDown(Entity<WFShipConsoleComponent> console, ref WFShipConsoleStandDownMessage args)
    {
        if (Roster(console, args.Actor, out var roster) && roster.Comp.Slots.ContainsKey(args.Slot))
            _roster.SetStoodDown(roster, args.Slot, args.Down, args.Actor);
        MarkChanged(console);
    }

    private void OnRestaff(Entity<WFShipConsoleComponent> console, ref WFShipConsoleRestaffMessage args)
    {
        if (Roster(console, args.Actor, out var roster))
        {
            roster.Comp.RestaffDelay = TimeSpan.FromSeconds(Math.Clamp(args.Delay, 0, MaxRestaffDelay));
            roster.Comp.RestaffsPerHour = Math.Clamp(args.PerHour, 0, MaxRestaffsPerHour);
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):actor} set automatic restaffing on {ToPrettyString(roster.Owner):grid} to {roster.Comp.RestaffDelay.TotalSeconds} s, {roster.Comp.RestaffsPerHour} an hour");
        }

        MarkChanged(console);
    }

    /// <summary>Changes the crew's standing orders; null leaves one as it is. False when the actor may not.</summary>
    public bool SetOrders(EntityUid console, EntityUid actor, WFShipMarineOrders? marines, WFGunnerPolicy? gunners, WFShipPilotStance? pilot, string? callsign)
    {
        if (marines is { } m && !Enum.IsDefined(m) || gunners is { } g && !Enum.IsDefined(g) || pilot is { } p && !Enum.IsDefined(p)
            || !Roster(console, actor, out var roster))
            return false;

        _behaviour.Set(roster, marines, gunners, pilot, callsign, actor);
        return true;
    }

    private void OnOrders(Entity<WFShipConsoleComponent> console, ref WFShipConsoleOrdersMessage args)
    {
        SetOrders(console, args.Actor, args.Marines, args.Gunners, args.Pilot, args.Callsign);
        MarkChanged(console);
    }

    private void OnJobSlots(Entity<WFShipConsoleComponent> console, ref WFShipConsoleJobSlotsMessage args)
    {
        if (Configuring(console, args.Actor, out var ship) && TryComp<WFShipJobsComponent>(ship, out var jobs)
            && jobs.Jobs.ContainsKey(args.Job))
            _jobs.SetSlots((ship, jobs), args.Job, Math.Clamp(args.Slots, 0, MaxJobSlots), args.Actor);
        MarkChanged(console);
    }

    private void OnReleaseJob(Entity<WFShipConsoleComponent> console, ref WFShipConsoleReleaseJobMessage args)
    {
        if (Configuring(console, args.Actor, out var ship) && GetEntity(args.Holder) is { Valid: true } holder
            && TryComp<WFShipJobHolderComponent>(holder, out var comp) && comp.Grid == ship.Owner)
            _jobs.Leave((holder, comp));
        MarkChanged(console);
    }

    private void OnPadAccess(Entity<WFShipConsoleComponent> console, ref WFShipConsolePadAccessMessage args)
    {
        if (TryGetShip(console, out var ship) && GetEntity(args.Pad) is { Valid: true } pad
            && TryComp<WFRecoveryPadComponent>(pad, out var comp) && Transform(pad).GridUid == ship.Owner)
            _pads.SetAccess((pad, comp), args.Actor, args.Access);
        MarkChanged(console);
    }

    #endregion

    #region Presets

    private void OnPreset(Entity<WFShipConsoleComponent> console, ref WFShipConsolePresetMessage args)
    {
        if (!Enum.IsDefined(args.Action) || !Configuring(console, args.Actor, out var ship))
            return;

        if (!WFShipPresetSystem.ValidName(args.Name))
        {
            _popup.PopupEntity(Loc.GetString("wf-ship-console-bad-preset", ("max", WFShipPresetSystem.MaxNameLength)), console, args.Actor, PopupType.MediumCaution);
            return;
        }

        // Each of these is a database write and a fresh read of the list.
        var now = _timing.CurTime;
        if (now < _presetAction.GetValueOrDefault(args.Actor))
            return;

        _presetAction[args.Actor] = now + PresetActionGap;
        if (_presetAction.Count > 64)
            _presetAction.Clear();

        _ = RunPreset(console, args.Actor, ship, args.Action, args.Name);
    }

    /// <summary>Saves, loads or deletes one of the actor's own presets, then reads their list again.</summary>
    private async Task RunPreset(EntityUid console, EntityUid actor, EntityUid ship, WFShipConsolePresetAction action, string name)
    {
        if (UserOf(actor) is not { } user || _presets.VesselOf(ship) is not { } vessel)
            return;

        try
        {
            var line = action switch
            {
                WFShipConsolePresetAction.Save => "wf-ship-console-preset-saved",
                WFShipConsolePresetAction.Load => "wf-ship-console-preset-loaded",
                _ => "wf-ship-console-preset-deleted",
            };
            switch (action)
            {
                case WFShipConsolePresetAction.Save:
                    var stored = await _db.GetShipCommandPresetsAsync(user.UserId, vessel);
                    if (stored.Count >= MaxPresets && !stored.Exists(p => p.Name == name))
                    {
                        line = "wf-ship-console-preset-full";
                        break;
                    }

                    if (TerminatingOrDeleted(ship))
                        return;

                    await _presets.Save(ship, user, name);
                    break;
                case WFShipConsolePresetAction.Load:
                    var data = await _presets.Fetch(user, vessel, name);
                    if (data == null || TerminatingOrDeleted(ship) || !Exists(actor) || !_command.CanConfigure(actor, ship))
                    {
                        line = "wf-ship-console-preset-missing";
                        break;
                    }

                    _presets.Apply(ship, data, actor, name);
                    break;
                case WFShipConsolePresetAction.Delete:
                    if (!await _db.DeleteShipCommandPresetAsync(user.UserId, vessel, name))
                        line = "wf-ship-console-preset-missing";
                    break;
            }

            if (Exists(console) && Exists(actor))
                _popup.PopupEntity(Loc.GetString(line, ("name", name)), console, actor);
        }
        catch (Exception e)
        {
            _log.GetSawmill("wf.shipcommand").Error($"Ship console preset {action} failed: {e}");
        }

        await LoadPresets(console, actor);
    }

    /// <summary>Reads the viewer's presets for the console's vessel, then sends them their state.</summary>
    private async Task LoadPresets(EntityUid console, EntityUid viewer)
    {
        if (!TryGetShip(console, out var ship) || _presets.VesselOf(ship) is not { } vessel || UserOf(viewer) is not { } user
            || !_command.CanConfigure(viewer, ship))
            return;

        try
        {
            var stored = await _db.GetShipCommandPresetsAsync(user.UserId, vessel);
            _presetLists[(user, vessel)] = stored.Select(p => new WFShipConsolePreset(p.Name, p.Saved)).ToList();
        }
        catch (Exception e)
        {
            _log.GetSawmill("wf.shipcommand").Error($"Ship console preset list failed: {e}");
            return;
        }

        if (Exists(console) && Exists(viewer) && _ui.IsUiOpen(console, WFShipConsoleUiKey.Key, viewer))
            SendState(console, viewer);
    }

    #endregion
}
