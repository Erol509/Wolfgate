using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Server._WG.BulkCargo;

/// <summary>
/// The deployable marker projector: clicking a tile places a corner at range, a second click pairs the markers into a
/// zone and spends the projector, and dropping the projector destroys it and its waiting corner. A marker can be removed
/// with an empty hand, which drops its zone.
/// </summary>
public sealed partial class WGCargoZoneProjectorSystem : EntitySystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WGCargoDeliveryZoneSystem _zones = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGCargoZoneProjectorComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WGCargoZoneProjectorComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<WGCargoZoneProjectorComponent, ComponentShutdown>(OnProjectorShutdown);
        SubscribeLocalEvent<WGCargoZoneCornerComponent, InteractHandEvent>(OnCornerRemoved);
    }

    private void OnAfterInteract(Entity<WGCargoZoneProjectorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.ClickLocation.IsValid(EntityManager))
            return;

        args.Handled = true;
        var user = args.User;

        if (!_interaction.InRangeUnobstructed(user, args.ClickLocation, ent.Comp.Range))
        {
            _popup.PopupEntity(Loc.GetString("wg-bulk-zone-too-far"), ent.Owner, user);
            return;
        }

        if (!_turf.TryGetTileRef(args.ClickLocation, out var tileRef) || _turf.IsSpace(tileRef.Value))
        {
            _popup.PopupEntity(Loc.GetString("wg-bulk-zone-space"), ent.Owner, user);
            return;
        }

        var grid = tileRef.Value.GridUid;
        if (ent.Comp.Pending is not { } pending || TerminatingOrDeleted(pending))
        {
            var marker = Spawn(ent.Comp.Marker, _turf.GetTileCenter(tileRef.Value));
            ent.Comp.Pending = marker;
            ent.Comp.PendingGrid = grid;
            _popup.PopupEntity(Loc.GetString("wg-bulk-zone-corner-set"), ent.Owner, user);
            return;
        }

        if (ent.Comp.PendingGrid != grid)
        {
            _popup.PopupEntity(Loc.GetString("wg-bulk-zone-other-grid"), ent.Owner, user);
            return;
        }

        var second = Spawn(ent.Comp.Marker, _turf.GetTileCenter(tileRef.Value));
        if (!_zones.TryPair(pending, second, out var id, out var error) || !_zones.TryZone(id, out var zone))
        {
            QueueDel(second);
            _popup.PopupEntity(Loc.GetString(error ?? "wg-bulk-zone-failed"), ent.Owner, user);
            return;
        }

        var count = _zones.Count(zone);
        _popup.PopupEntity(Loc.GetString("wg-bulk-zone-formed",
            ("free", count.Free), ("blocked", count.Blocked + count.Space)), ent.Owner, user);

        // One projector, one zone.
        ent.Comp.Pending = null;
        ent.Comp.PendingGrid = null;
        QueueDel(ent.Owner);
    }

    private void OnDropped(Entity<WGCargoZoneProjectorComponent> ent, ref DroppedEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        DeletePending(ent);
        _popup.PopupEntity(Loc.GetString("wg-bulk-zone-projector-dropped"), ent.Owner, args.User);
        QueueDel(ent.Owner);
    }

    private void OnProjectorShutdown(Entity<WGCargoZoneProjectorComponent> ent, ref ComponentShutdown args)
    {
        DeletePending(ent);
    }

    private void OnCornerRemoved(Entity<WGCargoZoneCornerComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _zones.Unpair(ent.Owner);
        _popup.PopupEntity(Loc.GetString("wg-bulk-zone-removed"), ent.Owner, args.User);
        QueueDel(ent.Owner);
    }

    /// <summary>Destroys the first corner a projector placed, if it still stands.</summary>
    private void DeletePending(Entity<WGCargoZoneProjectorComponent> ent)
    {
        if (ent.Comp.Pending is { } pending && !TerminatingOrDeleted(pending))
            QueueDel(pending);

        ent.Comp.Pending = null;
        ent.Comp.PendingGrid = null;
    }
}
