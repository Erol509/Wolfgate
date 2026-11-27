using System.Linq;
using System.Numerics;
using Content.Server.Hands.Systems;
using Content.Server.VendingMachines;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._WG.BulkCargo;
using Content.Shared._WG.TradeGoods;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.VendingMachines;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// The trade vendor's bulk order screen: the catalogue and delivery zones go out as shared state, while each viewer gets
/// their own balance and orders, and their cart quote over a message. Orders and cancellations are validated against
/// the same transaction the rest of the system uses.
/// </summary>
public sealed partial class WGCargoBulkOrderSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private VendingMachineSystem _vending = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly EntProtoId Projector = "WGCargoZoneProjector";
    private static readonly TimeSpan ProjectorCooldown = TimeSpan.FromSeconds(10);

    /// <summary>Shortest gap between quote requests from one viewer, so a spammy client can't re-price the shelf.</summary>
    private static readonly TimeSpan QuoteRequestInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Shortest gap between state refreshes from one viewer, so a spammy client can't re-scan every zone.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>When each player may take another zone projector.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextProjector = new();

    /// <summary>When each player may ask for another quote or screen refresh.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextQuote = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextRefresh = new();

    /// <summary>Reused lookup set for delivered crates in one zone.</summary>
    private readonly HashSet<EntityUid> _landedFound = new();

    private void InitializeUi()
    {
        SubscribeLocalEvent<WGTradeVendorComponent, BoundUIOpenedEvent>(OnBulkUiOpened);
        SubscribeLocalEvent<WGTradeVendorComponent, WGCargoBulkQuoteRequestMessage>(OnQuoteRequest);
        SubscribeLocalEvent<WGTradeVendorComponent, WGCargoBulkPlaceOrderMessage>(OnPlaceOrderMessage);
        SubscribeLocalEvent<WGTradeVendorComponent, WGCargoBulkCancelMessage>(OnCancelMessage);
        SubscribeLocalEvent<WGTradeVendorComponent, WGCargoBulkDispenseMessage>(OnDispenseMessage);
        SubscribeLocalEvent<WGTradeVendorComponent, WGCargoBulkRefreshMessage>(OnRefreshMessage);

        // The bulk window shows the vendor's ordinary supplies too; route its buy clicks through the vending system.
        Subs.BuiEvents<WGTradeVendorComponent>(WGCargoBulkUiKey.Key, subs =>
        {
            subs.Event<VendingMachineEjectMessage>(OnBulkEjectMessage);
        });
    }

    private void OnBulkEjectMessage(Entity<WGTradeVendorComponent> ent, ref VendingMachineEjectMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor)
            || !TryComp<VendingMachineComponent>(ent.Owner, out var machine))
            return;

        _vending.AuthorizedVend(ent.Owner, args.Actor, args.Type, args.ID, machine, args.Colors);
        SchedulePush(ent.Owner);
    }

    private void OnRefreshMessage(Entity<WGTradeVendorComponent> ent, ref WGCargoBulkRefreshMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor))
            return;

        var now = _timing.CurTime;
        if (_nextRefresh.TryGetValue(args.Actor, out var ready) && ready > now)
            return;

        _nextRefresh[args.Actor] = now + RefreshInterval;
        PushAll(ent.Owner);
    }

    private void OnBulkUiOpened(Entity<WGTradeVendorComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not WGCargoBulkUiKey || args.Actor == EntityUid.Invalid)
            return;

        PushAll(ent.Owner);
    }

    /// <summary>Sends the shared screen and each viewer's own balance and orders to everyone at the vendor.</summary>
    public void PushAll(EntityUid vendor)
    {
        if (TerminatingOrDeleted(vendor))
            return;

        var actors = _ui.GetActors(vendor, WGCargoBulkUiKey.Key).ToList();
        if (actors.Count == 0)
            return;

        var orders = OrdersOf(vendor);
        _ui.SetUiState(vendor, WGCargoBulkUiKey.Key, BuildState(vendor));
        foreach (var actor in actors)
            PushViewer(vendor, actor, orders);
    }

    /// <summary>What the vendor sells and where it can deliver, for every viewer.</summary>
    public WGCargoBulkState BuildState(EntityUid vendor)
    {
        var state = new WGCargoBulkState
        {
            Interval = Bulk(vendor).DeliveryInterval,
            MaxCrates = MaxCratesPerOrder,
        };

        if (!TryComp<VendingMachineComponent>(vendor, out var machine)
            || Transform(vendor).GridUid is not { } grid)
            return state;

        var stocked = _market.StockedGoods(grid);
        foreach (var (id, entry) in machine.Inventory)
        {
            if (entry.Amount == 0)
                continue;

            if (stocked.Contains(id) && _market.BuyQuote(vendor, id) is { } quote)
            {
                state.Products.Add(new WGCargoBulkProduct
                {
                    Id = id,
                    UnitPrice = quote.Amount,
                    Stock = _market.AvailableStock(grid, id),
                    Crate = true,
                    Type = entry.Type,
                });
            }
            else if (!stocked.Contains(id))
            {
                state.Products.Add(new WGCargoBulkProduct
                {
                    Id = id,
                    UnitPrice = 0,
                    Stock = entry.Amount == uint.MaxValue ? -1 : (int) entry.Amount,
                    Crate = false,
                    Type = entry.Type,
                });
            }
        }

        state.Products.Sort((a, b) => a.Crate != b.Crate
            ? a.Crate.CompareTo(b.Crate)
            : string.Compare(ProductName(a.Id), ProductName(b.Id), StringComparison.CurrentCulture));

        // The vendor's own station zones come first, so a busy screen never hides the obvious choices.
        var targets = Destinations(vendor).ToList();
        targets.Sort((a, b) => (a == grid ? 0 : 1).CompareTo(b == grid ? 0 : 1));

        foreach (var target in targets)
        {
            if (state.Zones.Count >= MaxZonesPerView)
                break;

            foreach (var zone in _zones.ZonesOn(target))
            {
                if (state.Zones.Count >= MaxZonesPerView)
                    break;

                var cells = _zones.Describe(zone);
                var zoneLanded = LandedIn(target, zone);
                state.Zones.Add(new WGCargoBulkZone
                {
                    Id = zone.Id,
                    Grid = GetNetEntity(target),
                    Label = ZoneLabel(grid, zone),
                    Ship = target != grid,
                    Min = zone.Min,
                    Max = zone.Max,
                    Free = cells.Free,
                    Blocked = cells.Blocked.Where(tile => !zoneLanded.Contains(tile)).ToList(),
                    Reserved = cells.Reserved,
                    Landed = zoneLanded,
                });
            }
        }

        // The counter is always offered, even when every marker zone has been used or hidden by the view cap.
        var counterCells = CounterCells(vendor, grid);
        var counterFree = new List<Vector2i>();
        var counterBlocked = new List<Vector2i>();
        var counterMin = new Vector2i(0, 0);
        var counterMax = new Vector2i(0, 0);
        for (var i = 0; i < counterCells.Count; i++)
        {
            var tile = counterCells[i];
            if (i == 0)
            {
                counterMin = tile;
                counterMax = tile;
            }
            else
            {
                counterMin = Vector2i.ComponentMin(counterMin, tile);
                counterMax = Vector2i.ComponentMax(counterMax, tile);
            }

            if (_zones.ClassifyCell(grid, tile) == WGCargoCellState.Free)
                counterFree.Add(tile);
            else
                counterBlocked.Add(tile);
        }

        var counterZone = new WGCargoZone(default, default, grid, WGCargoBulkDestinations.Counter,
            counterMin, counterMax);
        var counterLanded = LandedIn(grid, counterZone);
        state.Zones.Add(new WGCargoBulkZone
        {
            Id = WGCargoBulkDestinations.Counter,
            Grid = GetNetEntity(grid),
            Label = Loc.GetString("wg-bulk-zone-counter", ("vendor", Name(vendor))),
            Ship = false,
            Min = counterMin,
            Max = counterMax,
            Free = counterFree,
            Blocked = counterBlocked.Where(tile => !counterLanded.Contains(tile)).ToList(),
            Reserved = new List<Vector2i>(),
            Landed = counterLanded,
        });

        return state;
    }

    /// <summary>The vendor's orders that belong to one viewer, with the cells and timing the preview fills.</summary>
    public List<WGCargoBulkOrderEntry> ViewerOrders(EntityUid vendor, EntityUid viewer)
    {
        return ViewerOrders(vendor, viewer, OrdersOf(vendor));
    }

    /// <summary>The same list from an order snapshot shared by every viewer of one push.</summary>
    private List<WGCargoBulkOrderEntry> ViewerOrders(
        EntityUid vendor,
        EntityUid viewer,
        List<WGCargoBulkOrder> orders)
    {
        var now = _timing.CurTime;
        var interval = Bulk(vendor).DeliveryInterval;
        var entries = new List<WGCargoBulkOrderEntry>();

        foreach (var order in orders)
        {
            if (order.Buyer != viewer)
                continue;

            var entry = new WGCargoBulkOrderEntry
            {
                Id = order.Id,
                Zone = order.Zone,
                ZoneLabel = order.ZoneLabel,
                Cells = new List<Vector2i>(order.Cells),
                Delivered = order.Delivered,
                Total = order.Goods.Count,
                NextIn = order.NextAt > now ? order.NextAt - now : TimeSpan.Zero,
                Interval = interval,
            };

            foreach (var group in order.Goods.GroupBy(good => good))
                entry.Lines.Add(new WGCargoBulkOrderLine { Id = group.Key, Count = group.Count() });

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Hands a zone projector to a player standing at the vendor, at most once every few seconds.</summary>
    public bool TryDispenseProjector(EntityUid vendor, EntityUid actor, out string? error)
    {
        error = null;
        if (Transform(vendor).GridUid is not { } vendorGrid || Transform(actor).GridUid != vendorGrid)
        {
            error = "wg-bulk-error-far";
            return false;
        }

        var now = _timing.CurTime;
        if (_nextProjector.TryGetValue(actor, out var ready) && ready > now)
        {
            error = "wg-bulk-error-dispense";
            return false;
        }

        _nextProjector[actor] = now + ProjectorCooldown;
        var projector = Spawn(Projector, Transform(actor).Coordinates);
        _hands.TryPickupAnyHand(actor, projector);
        return true;
    }

    /// <summary>The name players know a delivery zone by: its station or ship, and where its corner stands.</summary>
    private string ZoneLabel(EntityUid vendorGrid, WGCargoZone zone)
    {
        return zone.Grid == vendorGrid
            ? Loc.GetString("wg-bulk-zone-station", ("ship", Name(zone.Grid)), ("x", zone.Min.X), ("y", zone.Min.Y))
            : Loc.GetString("wg-bulk-zone-ship", ("ship", Name(zone.Grid)));
    }

    /// <summary>Floor tiles in the one-tile ring around a vendor, the spots a counter order may use.</summary>
    private List<Vector2i> CounterCells(EntityUid vendor, EntityUid grid)
    {
        var cells = new List<Vector2i>(8);
        if (!TryComp<MapGridComponent>(grid, out var gridComp)
            || !TryComp<TransformComponent>(vendor, out var xform))
            return cells;

        var centre = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var tile = new Vector2i(centre.X + x, centre.Y + y);
                if (_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) && !tileRef.Tile.IsEmpty)
                    cells.Add(tile);
            }
        }

        return cells;
    }

    /// <summary>
    /// Tiles of one zone holding a crate this system already delivered. The grid broadphase is asked only for the zone's
    /// box, so a long round with thousands of delivered crates doesn't rescan them all on every screen push.
    /// </summary>
    private List<Vector2i> LandedIn(EntityUid grid, WGCargoZone zone)
    {
        var landed = new List<Vector2i>();
        if (!TryComp<MapGridComponent>(grid, out var gridComp)
            || !TryComp<TransformComponent>(grid, out var gridXform))
            return landed;

        var local = new Box2(
            new Vector2(zone.Min.X, zone.Min.Y),
            new Vector2(zone.Max.X + 1, zone.Max.Y + 1));
        var world = _transform.GetWorldMatrix(gridXform).TransformBox(local);

        _landedFound.Clear();
        _lookup.GetEntitiesIntersecting(grid, world, _landedFound, LookupFlags.Dynamic | LookupFlags.Static);

        foreach (var uid in _landedFound)
        {
            if (!HasComp<WGBulkPurchasedComponent>(uid)
                || !TryComp<TransformComponent>(uid, out var xform)
                || xform.GridUid != grid
                || xform.ParentUid != grid)
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (tile.X >= zone.Min.X && tile.X <= zone.Max.X && tile.Y >= zone.Min.Y && tile.Y <= zone.Max.Y)
                landed.Add(tile);
        }

        return landed;
    }

    private string ProductName(string id)
    {
        return _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
    }

    private void PushViewer(EntityUid vendor, EntityUid viewer, List<WGCargoBulkOrder> orders)
    {
        _bank.TryGetBalance(viewer, out var balance);
        _ui.ServerSendUiMessage(vendor, WGCargoBulkUiKey.Key,
            new WGCargoBulkBalanceMessage { Balance = balance }, viewer);
        _ui.ServerSendUiMessage(vendor, WGCargoBulkUiKey.Key,
            new WGCargoBulkOrdersMessage { Orders = ViewerOrders(vendor, viewer, orders) }, viewer);
    }

    private void OnQuoteRequest(Entity<WGTradeVendorComponent> ent, ref WGCargoBulkQuoteRequestMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor))
            return;

        var now = _timing.CurTime;
        if (_nextQuote.TryGetValue(args.Actor, out var ready) && ready > now)
        {
            _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key,
                new WGCargoBulkQuoteMessage { Error = "wg-bulk-error-wait", Revision = args.Revision }, args.Actor);
            return;
        }

        _nextQuote[args.Actor] = now + QuoteRequestInterval;

        var reply = new WGCargoBulkQuoteMessage { Revision = args.Revision };

        // Bound what one message can make the server price, before touching the market.
        long total = 0;
        foreach (var (_, quantity) in args.Items)
        {
            if (quantity <= 0)
                continue;

            total += quantity;
            if (total > MaxCratesPerOrder)
                break;
        }

        if (args.Items.Count > MaxCartLines || total > MaxCratesPerOrder)
        {
            reply.Error = "wg-bulk-error-cart";
            _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key, reply, args.Actor);
            return;
        }

        var cart = new List<WGTradeBulkLine>();
        foreach (var (id, quantity) in args.Items)
        {
            if (quantity > 0)
                cart.Add(new WGTradeBulkLine(id, quantity));
        }

        WGTradeBulkQuote? priced = null;
        var quote = cart.Count == 0
            ? null
            : Quote(ent.Owner, args.Actor, cart, args.Zone, out priced);

        if (quote is not { } placed)
        {
            reply.Error = cart.Count == 0 || priced == null ? "wg-bulk-error-cart" : "wg-bulk-error-zone";
        }
        else
        {
            reply.Quote = placed;
            reply.Total = priced!.Total;
            reply.Crates = priced.Crates;
            foreach (var line in priced.Lines)
            {
                reply.Lines.Add(new WGCargoBulkQuoteLine
                {
                    Id = line.Good,
                    Quantity = line.Quantity,
                    First = line.First,
                    Last = line.Last,
                    Subtotal = line.Subtotal,
                });
            }
        }

        _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key, reply, args.Actor);
    }

    private void OnPlaceOrderMessage(Entity<WGTradeVendorComponent> ent, ref WGCargoBulkPlaceOrderMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor))
            return;

        var id = PlaceOrder(ent.Owner, args.Actor, args.Quote, args.ExpectedTotal, out var error);
        _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key,
            new WGCargoBulkOrderResultMessage
            {
                Success = id != null,
                Error = id == null ? error ?? "wg-bulk-error-order" : null,
                Placed = true,
            }, args.Actor);

        SchedulePush(ent.Owner);
    }

    private void OnCancelMessage(Entity<WGTradeVendorComponent> ent, ref WGCargoBulkCancelMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor))
            return;

        var cancelled = TryCancel(args.Actor, args.Order);
        _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key,
            new WGCargoBulkOrderResultMessage
            {
                Success = cancelled,
                Error = cancelled ? null : "wg-bulk-error-cancel",
            }, args.Actor);

        SchedulePush(ent.Owner);
    }

    private void OnDispenseMessage(Entity<WGTradeVendorComponent> ent, ref WGCargoBulkDispenseMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor))
            return;

        var handed = TryDispenseProjector(ent.Owner, args.Actor, out var error);
        _ui.ServerSendUiMessage(ent.Owner, WGCargoBulkUiKey.Key,
            new WGCargoBulkDispenseResultMessage { Success = handed, Error = error }, args.Actor);
    }

    /// <summary>Whether a message came from someone standing at the open vendor with access to it.</summary>
    private bool CanUse(EntityUid vendor, EntityUid actor)
    {
        if (actor == EntityUid.Invalid || TerminatingOrDeleted(actor) || TerminatingOrDeleted(vendor))
            return false;

        if (!_ui.IsUiOpen((vendor, (UserInterfaceComponent?) null), WGCargoBulkUiKey.Key, actor))
            return false;

        if (!_interaction.InRangeUnobstructed(actor, vendor, 2.5f, popup: false))
            return false;

        return !TryComp<AccessReaderComponent>(vendor, out var reader) || _access.IsAllowed(actor, vendor, reader);
    }
}
