using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Stack;
using Content.Shared._WF.WolfChem.Narcotics.Delivery;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Temperature;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// Snorting and bongs: baggies lay powder lines on tables, a line goes up the nose after a short do-after, and a lit
/// bong takes one hit at a time from its bowl.
/// </summary>
public sealed class WFDeliverySystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private FoodSystem _food = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WFBaggieSystem _baggie = default!;
    [Dependency] private WFSecondhandSmokeSystem _secondhand = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBaggieComponent, AfterInteractEvent>(OnBaggieAfterInteract);
        SubscribeLocalEvent<WFPowderLineComponent, ActivateInWorldEvent>(OnLineActivate);
        SubscribeLocalEvent<WFPowderLineComponent, WFSnortLineDoAfterEvent>(OnSnortDoAfter);

        SubscribeLocalEvent<WFBongComponent, ComponentInit>(OnBongInit);
        SubscribeLocalEvent<WFBongComponent, ComponentRemove>(OnBongRemove);
        SubscribeLocalEvent<WFBongComponent, InteractUsingEvent>(OnBongInteractUsing);
        SubscribeLocalEvent<WFBongComponent, UseInHandEvent>(OnBongUseInHand);
        SubscribeLocalEvent<WFBongComponent, WFBongHitDoAfterEvent>(OnBongDoAfter);
        SubscribeLocalEvent<WFBongComponent, EntInsertedIntoContainerMessage>(OnBowlLoaded);
        SubscribeLocalEvent<WFBongComponent, EntRemovedFromContainerMessage>(OnBowlEmptied);
        SubscribeLocalEvent<WFBongComponent, ExaminedEvent>(OnBongExamined);
    }

    private void OnBaggieAfterInteract(Entity<WFBaggieComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        // A loose solid is scooped straight in.
        if (TryComp<StorageComponent>(ent, out var storage)
            && HasComp<ItemComponent>(target)
            && !_container.IsEntityInContainer(target)
            && _storage.CanInsert(ent, target, out _, storage))
        {
            args.Handled = true;
            if (_storage.PlayerInsertEntityInWorld((ent, storage), args.User, target))
                _popup.PopupEntity(Loc.GetString("wf-baggie-solid-in", ("item", target)), ent, args.User);
            return;
        }

        if (!HasComp<PlaceableSurfaceComponent>(target))
            return;

        // With no powder to pour, a solid is set down instead.
        if (!HasPowder(ent) && _baggie.TryTakeSolid(ent, out var solid))
        {
            _transform.SetCoordinates(solid, args.ClickLocation);
            _popup.PopupEntity(Loc.GetString("wf-baggie-solid-tipped", ("item", solid)), solid, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = TryPlaceLine(ent, args.User, args.ClickLocation) != null;
    }

    private bool HasPowder(Entity<WFBaggieComponent> baggie)
    {
        return _solutions.TryGetSolution(baggie.Owner, baggie.Comp.Solution, out _, out var solution)
               && solution.Volume > FixedPoint2.Zero;
    }

    /// <summary>Lays out a line from a baggie at a spot. Returns the line, or null if the baggie is empty.</summary>
    public EntityUid? TryPlaceLine(Entity<WFBaggieComponent> baggie, EntityUid user, EntityCoordinates coords)
    {
        if (!_solutions.TryGetSolution(baggie.Owner, baggie.Comp.Solution, out var soln, out var solution)
            || solution.Volume <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("wf-baggie-empty"), baggie, user);
            return null;
        }

        var portion = _solutions.SplitSolution(soln.Value, FixedPoint2.Min(baggie.Comp.LineAmount, solution.Volume));
        var line = Spawn(baggie.Comp.Line, coords);
        if (!TryComp<WFPowderLineComponent>(line, out var lineComp)
            || !_solutions.TryGetSolution(line, lineComp.Solution, out var lineSoln, out _))
        {
            Del(line);
            return null;
        }

        _solutions.TryAddSolution(lineSoln.Value, portion);
        _popup.PopupEntity(Loc.GetString("wf-baggie-line-placed"), line, user);
        return line;
    }

    private void OnLineActivate(Entity<WFPowderLineComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || _food.IsMouthBlocked(args.User, args.User))
            return;

        args.Handled = true;
        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WFSnortLineDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("wf-line-snort-start"), args.User, args.User);
    }

    private void OnSnortDoAfter(Entity<WFPowderLineComponent> ent, ref WFSnortLineDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = Snort(ent, args.User);
    }

    /// <summary>Moves a line's solution into the user's bloodstream and removes the line.</summary>
    public bool Snort(Entity<WFPowderLineComponent> line, EntityUid user)
    {
        if (!_solutions.TryGetSolution(line.Owner, line.Comp.Solution, out var soln, out _))
            return false;

        var dose = _solutions.SplitSolution(soln.Value, soln.Value.Comp.Solution.Volume);
        _bloodstream.TryAddToChemicals(user, dose);
        _popup.PopupEntity(Loc.GetString("wf-line-snort-done"), user, user);
        QueueDel(line);
        return true;
    }

    private void OnBongInit(Entity<WFBongComponent> ent, ref ComponentInit args)
    {
        _slots.AddItemSlot(ent, WFBongComponent.BowlSlotId, ent.Comp.Bowl);
        UpdateBong(ent);
    }

    private void OnBongRemove(Entity<WFBongComponent> ent, ref ComponentRemove args)
    {
        _slots.RemoveItemSlot(ent, ent.Comp.Bowl);
    }

    private void SetLit(Entity<WFBongComponent> ent, bool lit)
    {
        ent.Comp.Lit = lit;
        UpdateBong(ent);
    }

    private void UpdateBong(Entity<WFBongComponent> ent)
    {
        _appearance.SetData(ent, WFBongVisuals.Loaded, ent.Comp.Bowl.Item != null);
        _appearance.SetData(ent, WFBongVisuals.Lit, ent.Comp.Lit);
    }

    private void OnBongExamined(Entity<WFBongComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Lit)
            args.PushMarkup(Loc.GetString("wf-bong-examine-lit"));
    }

    /// <summary>A stack loads one piece; the rest goes back to whoever holds the bong, or lands beside it.</summary>
    private void OnBowlLoaded(Entity<WFBongComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != WFBongComponent.BowlSlotId)
            return;

        UpdateBong(ent);
        if (!TryComp<StackComponent>(args.Entity, out var stack) || stack.Count <= 1)
            return;

        if (_stack.Split(args.Entity, stack.Count - 1, Transform(ent).Coordinates, stack) is not { } rest)
            return;

        var holder = _container.TryGetContainingContainer((ent.Owner, null, null), out var held) ? held.Owner : (EntityUid?) null;
        _hands.PickupOrDrop(holder, rest);
    }

    private void OnBowlEmptied(Entity<WFBongComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == WFBongComponent.BowlSlotId)
            SetLit(ent, false);
    }

    private void OnBongInteractUsing(Entity<WFBongComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var hot = new IsHotEvent();
        RaiseLocalEvent(args.Used, hot);
        if (!hot.IsHot)
            return;

        args.Handled = true;
        TryLight(ent, args.User);
    }

    /// <summary>Lights a loaded bowl.</summary>
    public bool TryLight(Entity<WFBongComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Bowl.Item == null)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wf-bong-bowl-empty"), ent, user.Value);
            return false;
        }

        SetLit(ent, true);
        if (user != null)
            _popup.PopupEntity(Loc.GetString("wf-bong-lit"), ent, user.Value);
        return true;
    }

    private void OnBongUseInHand(Entity<WFBongComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (!CanHit(ent, args.User))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new WFBongHitDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private bool CanHit(Entity<WFBongComponent> ent, EntityUid user)
    {
        if (!ent.Comp.Lit || ent.Comp.Bowl.Item == null)
        {
            _popup.PopupEntity(Loc.GetString("wf-bong-not-lit"), ent, user);
            return false;
        }

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.Water, out _, out var water)
            || water.GetTotalPrototypeQuantity("Water") < ent.Comp.MinWater)
        {
            _popup.PopupEntity(Loc.GetString("wf-bong-no-water"), ent, user);
            return false;
        }

        return !_food.IsMouthBlocked(user, user);
    }

    private void OnBongDoAfter(Entity<WFBongComponent> ent, ref WFBongHitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = Hit(ent, args.User);
    }

    /// <summary>Takes one hit from a lit bowl into the user's bloodstream, less the share left in the air, and puffs out
    /// a little smoke.</summary>
    public bool Hit(Entity<WFBongComponent> ent, EntityUid user)
    {
        if (!CanHit(ent, user) || ent.Comp.Bowl.Item is not { } bowl)
            return false;

        var hit = new Solution();
        var wanted = ent.Comp.HitAmount;
        foreach (var (_, soln) in _solutions.EnumerateSolutions(bowl))
        {
            if (wanted <= FixedPoint2.Zero)
                break;

            var taken = _solutions.SplitSolution(soln, FixedPoint2.Min(wanted, soln.Comp.Solution.Volume));
            wanted -= taken.Volume;
            hit.AddSolution(taken, null);
        }

        if (!BowlHasContents(bowl))
        {
            SetLit(ent, false);
            QueueDel(bowl);
        }

        if (hit.Volume <= FixedPoint2.Zero)
            return false;

        _secondhand.Exhale(ent, hit);
        _reactive.DoEntityReaction(user, hit, ReactionMethod.Ingestion);
        _bloodstream.TryAddToChemicals(user, hit);
        _popup.PopupEntity(Loc.GetString("wf-bong-hit"), user, user);
        Puff(ent);
        return true;
    }

    private bool BowlHasContents(EntityUid bowl)
    {
        foreach (var (_, soln) in _solutions.EnumerateSolutions(bowl))
        {
            if (soln.Comp.Solution.Volume > FixedPoint2.Zero)
                return true;
        }

        return false;
    }

    private void Puff(Entity<WFBongComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid)
            return;

        var coords = _map.MapToGrid(grid, _transform.GetMapCoordinates(ent)).SnapToGrid(EntityManager);
        var cloud = Spawn(ent.Comp.Puff, coords);
        if (!TryComp<SmokeComponent>(cloud, out var smoke))
        {
            Del(cloud);
            return;
        }

        var air = new Solution();
        air.AddReagent("Water", FixedPoint2.New(0.5));
        _smoke.StartSmoke(cloud, air, 2f, 0, smoke);
    }
}
