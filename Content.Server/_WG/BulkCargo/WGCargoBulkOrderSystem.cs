using System.Numerics;
using Content.Shared.GameTicking;
using Content.Server._NF.Bank;
using Content.Server.Shuttles.Components;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WG.BulkCargo;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// Bulk crate orders at a station's trade vendors. A cart is quoted from the market's own shelf curve for a set of
/// markers, held briefly as a server-issued quote, and accepted as one transaction: bank payment, stock off the shelf,
/// and cells reserved for the delivery queue to fill one crate at a time.
/// </summary>
public sealed partial class WGCargoBulkOrderSystem : EntitySystem
{
    [Dependency] private WGTradeMarketSystem _market = default!;
    [Dependency] private WGCargoDeliveryZoneSystem _zones = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far a ship's hull may sit from the counter and still be offered as a delivery.</summary>
    public const float ShipRange = 100f;

    /// <summary>Most crates one order may take.</summary>
    public const int MaxCratesPerOrder = 200;

    /// <summary>Most orders one buyer may keep running at once, so one player can't flood a vendor's queue.</summary>
    public const int MaxActiveOrdersPerBuyer = 10;

    /// <summary>Most zones one screen lists, so marker spam on many ships can't make the state message huge.</summary>
    public const int MaxZonesPerView = 64;

    /// <summary>Most distinct goods one quote request may name, so a spammy client can't make the server loop.</summary>
    public const int MaxCartLines = 64;

    /// <summary>How long a quote stays good; the server revalidates it on confirmation anyway.</summary>
    public static readonly TimeSpan QuoteLifetime = TimeSpan.FromSeconds(30);

    /// <summary>A priced cart held between its quote and its confirmation.</summary>
    private sealed class Hold
    {
        public EntityUid Vendor;
        public EntityUid Buyer;
        public List<WGTradeBulkLine> Cart = new();
        public Guid Zone;
        public int Total;
        public int Crates;
        public TimeSpan Expires;
    }

    private readonly Dictionary<Guid, Hold> _quotes = new();
    private readonly Dictionary<Guid, WGCargoBulkOrder> _orders = new();
    private readonly Dictionary<EntityUid, List<Guid>> _byVendor = new();

    /// <summary>The one live quote each actor holds, so repeated cart changes can't pile up holds.</summary>
    private readonly Dictionary<EntityUid, Guid> _actorQuote = new();

    /// <summary>Reused list of quote ids that have expired, so the tick loop does not allocate.</summary>
    private readonly List<Guid> _expiredQuotes = new();

    /// <summary>Reused list of order ids whose zone was just taken down.</summary>
    private readonly List<Guid> _zoneStops = new();

    /// <summary>Vendors whose screen one delivery changed, sent in one batch rather than once per crate.</summary>
    private readonly HashSet<EntityUid> _pendingPushes = new();
    private TimeSpan _nextPush;

    /// <summary>Refunds a bank refused, kept by payer id so a new body can still collect after the old one is gone.</summary>
    private readonly Dictionary<NetUserId, int> _unpaidRefunds = new();
    private readonly List<NetUserId> _refundUsers = new();
    private TimeSpan _nextRefundPayout;

    /// <summary>Cached destination sets for the current tick, so a state push or quote does not re-scan every dock.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _destinations = new();

    /// <summary>The dock-connected station network each root grid belongs to this tick.</summary>
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _stationNetworks = new();
    private GameTick _destinationsTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<WGCargoZoneRemovedEvent>(OnZoneRemoved);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        InitializeUi();
    }

    private void OnCleanup(RoundRestartCleanupEvent args)
    {
        _quotes.Clear();
        _orders.Clear();
        _byVendor.Clear();
        _actorQuote.Clear();
        _zoneStops.Clear();
        _pendingPushes.Clear();
        _nextPush = TimeSpan.Zero;
        _unpaidRefunds.Clear();
        _refundUsers.Clear();
        _nextRefundPayout = TimeSpan.Zero;
        _nextProjector.Clear();
        _nextQuote.Clear();
        _nextRefresh.Clear();
        _destinations.Clear();
        _stationNetworks.Clear();
        _destinationsTick = GameTick.Zero;
    }

    /// <summary>Stops every order whose delivery zone was removed, refunding the crates that never arrived.</summary>
    private void OnZoneRemoved(WGCargoZoneRemovedEvent args)
    {
        if (_orders.Count == 0)
            return;

        _zoneStops.Clear();
        foreach (var (id, order) in _orders)
        {
            if (order.Zone == args.Zone && order.Status == WGCargoBulkOrderStatus.Delivering)
                _zoneStops.Add(id);
        }

        foreach (var id in _zoneStops)
        {
            if (!_orders.TryGetValue(id, out var order))
                continue;

            Stop(order, refundPaid: false);
            SchedulePush(order.Vendor);
        }
    }

    /// <summary>When a payer gets a body, hand them any refund their old body could not take.</summary>
    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (!_unpaidRefunds.TryGetValue(args.Player.UserId, out var amount))
            return;

        if (_bank.TryBankDeposit(args.Mob, amount, tax: false))
            _unpaidRefunds.Remove(args.Player.UserId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        ExpireQuotes();
        Deliver();
        PushDue();
        PayoutRefunds();
    }

    /// <summary>Drops quotes nobody confirmed in time.</summary>
    private void ExpireQuotes()
    {
        if (_quotes.Count == 0)
            return;

        var now = _timing.CurTime;
        _expiredQuotes.Clear();
        foreach (var (id, hold) in _quotes)
        {
            if (hold.Expires < now)
                _expiredQuotes.Add(id);
        }

        foreach (var id in _expiredQuotes)
        {
            if (!_quotes.Remove(id, out var hold))
                continue;

            if (_actorQuote.TryGetValue(hold.Buyer, out var active) && active == id)
                _actorQuote.Remove(hold.Buyer);
        }
    }

    /// <summary>
    /// Prices a cart for one buyer and destination, and holds it as a quote id for confirmation. Null if the vendor
    /// can't fill it, the zone isn't on the vendor's station or a ship docked to it, or too few cells are free.
    /// </summary>
    public Guid? Quote(EntityUid vendor, EntityUid buyer, IReadOnlyList<WGTradeBulkLine> cart, Guid zone)
    {
        return Quote(vendor, buyer, cart, zone, out _);
    }

    /// <summary>The same quote, handing back the pricing it was accepted at so a caller need not price the cart twice.</summary>
    public Guid? Quote(
        EntityUid vendor,
        EntityUid buyer,
        IReadOnlyList<WGTradeBulkLine> cart,
        Guid zone,
        out WGTradeBulkQuote? priced)
    {
        priced = null;
        if (cart.Count == 0
            || cart.Count > MaxCartLines
            || !HasComp<WGTradeVendorComponent>(vendor)
            || Transform(vendor).GridUid is not { } grid)
            return null;

        var counter = zone == WGCargoBulkDestinations.Counter;
        var area = default(WGCargoZone);
        if (counter)
        {
            if (CounterCells(vendor, grid).Count == 0)
                return null;
        }
        else if (!_zones.TryZone(zone, out area) || !Destinations(vendor).Contains(area.Grid))
        {
            return null;
        }

        priced = _market.CartQuote(vendor, cart);
        if (priced is null || priced.Crates <= 0 || priced.Crates > MaxCratesPerOrder)
            return null;

        if (!counter && _zones.FreeCount(area) < priced.Crates)
            return null;

        if (_actorQuote.Remove(buyer, out var previous))
            _quotes.Remove(previous);

        var id = Guid.NewGuid();
        _quotes[id] = new Hold
        {
            Vendor = vendor,
            Buyer = buyer,
            Cart = new List<WGTradeBulkLine>(cart),
            Zone = zone,
            Total = priced.Total,
            Crates = priced.Crates,
            Expires = _timing.CurTime + QuoteLifetime,
        };
        _actorQuote[buyer] = id;

        return id;
    }

    /// <summary>A paid order, or null once it is done, cancelled or unknown.</summary>
    public bool TryGetOrder(Guid id, out WGCargoBulkOrder order)
    {
        return _orders.TryGetValue(id, out order!);
    }

    /// <summary>The orders a vendor still owes crates for, oldest first.</summary>
    public List<WGCargoBulkOrder> OrdersOf(EntityUid vendor)
    {
        var orders = new List<WGCargoBulkOrder>();
        if (!_byVendor.TryGetValue(vendor, out var ids))
            return orders;

        foreach (var id in ids)
        {
            if (_orders.TryGetValue(id, out var order))
                orders.Add(order);
        }

        return orders;
    }

    /// <summary>Whether a buyer may open a vendor's bulk order screen: a bank account, and not an ironman character.</summary>
    public bool CanTrade(EntityUid buyer)
    {
        return !HasComp<IronmanComponent>(buyer)
               && _players.TryGetSessionByEntity(buyer, out _)
               && _bank.TryGetBalance(buyer, out _);
    }

    /// <summary>
    /// The grids a vendor may deliver to: its own station network, however the station anchors the cargo set, and any
    /// ship whose hull lies near the counter. A dock link alone is not enough for a ship, since a berth on the far side
    /// of a station is beyond the cargo bay's reach; measuring the hull rather than the grid origin lets a long hauler
    /// dock at the near end and still qualify.
    /// </summary>
    public HashSet<EntityUid> Destinations(EntityUid vendor)
    {
        if (_destinationsTick != _timing.CurTick)
        {
            _destinationsTick = _timing.CurTick;
            _destinations.Clear();
            _stationNetworks.Clear();
        }

        if (_destinations.TryGetValue(vendor, out var cached))
            return cached;

        var set = new HashSet<EntityUid>();
        if (Transform(vendor).GridUid is not { } root)
            return set;

        set.Add(root);

        if (!_stationNetworks.TryGetValue(root, out var network))
        {
            // Docking links join the station's own modules; shuttles are considered by distance below.
            var links = new Dictionary<EntityUid, List<EntityUid>>();
            var docks = EntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (docks.MoveNext(out _, out var dock, out var xform))
            {
                if (xform.GridUid is not { } here
                    || dock.DockedWith is not { } other
                    || !TryComp(other, out TransformComponent? otherXform)
                    || otherXform.GridUid is not { } there
                    || here == there)
                    continue;

                AddLink(links, here, there);
                AddLink(links, there, here);
            }

            network = new HashSet<EntityUid>();
            var queue = new Queue<EntityUid>();
            queue.Enqueue(root);
            while (queue.TryDequeue(out var grid))
            {
                if (!network.Add(grid))
                    continue;

                if (links.TryGetValue(grid, out var others))
                {
                    foreach (var other in others)
                        queue.Enqueue(other);
                }
            }

            _stationNetworks[root] = network;
        }

        foreach (var grid in network)
        {
            if (!HasComp<ShuttleComponent>(grid))
                set.Add(grid);
        }

        if (TryComp<TransformComponent>(vendor, out var vendorXform))
        {
            var point = _transform.GetWorldPosition(vendorXform);
            var map = vendorXform.MapID;
            var shuttles = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
            while (shuttles.MoveNext(out var ship, out _, out var xform))
            {
                if (xform.MapID == map && WithinShipRange(ship, xform, point))
                    set.Add(ship);
            }
        }

        _destinations[vendor] = set;
        return set;
    }

    /// <summary>Whether a ship's hull, not its grid origin, is close enough to the counter to take a delivery.</summary>
    public bool WithinShipRange(EntityUid ship, TransformComponent xform, Vector2 point)
    {
        if (TryComp<MapGridComponent>(ship, out var grid) && grid.LocalAABB.Size.LengthSquared() > 0.001f)
        {
            var world = _transform.GetWorldMatrix(xform).TransformBox(grid.LocalAABB);
            var closest = new Vector2(
                Math.Clamp(point.X, world.Left, world.Right),
                Math.Clamp(point.Y, world.Bottom, world.Top));
            return Vector2.Distance(point, closest) <= ShipRange;
        }

        return Vector2.Distance(_transform.GetWorldPosition(xform), point) <= ShipRange;
    }

    private static void AddLink(Dictionary<EntityUid, List<EntityUid>> links, EntityUid from, EntityUid to)
    {
        if (!links.TryGetValue(from, out var list))
        {
            list = new List<EntityUid>();
            links[from] = list;
        }

        if (!list.Contains(to))
            list.Add(to);
    }
}
