using Content.Server._WF.NpcCrew.Components;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Materials;
using Content.Server.Power.Generator;
using Content.Server.Stack;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ame.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// The engineer's work: keeping the ship's plant running from its own stores. A generator low on fuel is fed from the
/// fuel stowed aboard, the reactor gets a fresh jar and is set injecting, and a plant with fuel in it is switched on.
/// Nothing is conjured: when the stores are gone, or the engineer is dead, the ship goes dark.
/// </summary>
public sealed partial class WFCrewWorkSystem
{
    [Dependency] private GeneratorSystem _generators = default!;
    [Dependency] private MaterialStorageSystem _materials = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private EntityStorageSystem _storage = default!;

    private static readonly TimeSpan TendInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextTend;

    /// <summary>Fuel left in a generator, in its own units, below which the engineer brings more.</summary>
    private const float LowFuel = 10f;

    /// <summary>Antimatter left in the reactor's jar below which it is swapped for a fresh one.</summary>
    private const int LowAntimatter = 200;

    /// <summary>Stacks of solid fuel stowed per generator, and jars per reactor, when a ship is stocked.</summary>
    private const int StoresPerMachine = 2;

    /// <summary>Sheets in each stowed stack: small enough to go into a generator in one, whatever is left in it.</summary>
    private const int StackSize = 10;

    private static readonly EntProtoId StoresCrate = "WFCrewFuelStores";
    private static readonly EntProtoId Jar = "AmeJar";
    private static readonly EntProtoId WeldingFuelCan = "JerryCanWeldingFuel";

    /// <summary>Gives every idle engineer with a plant in need the job of seeing to it.</summary>
    private void TendPower()
    {
        if (_timing.CurTime < _nextTend)
            return;

        _nextTend = _timing.CurTime + TendInterval;
        var shelter = EntityManager.System<WFCrewShelterSystem>();
        var query = EntityQueryEnumerator<WFCrewEngineerComponent, WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (_jobs.ContainsKey(uid) || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || crew.Duty != WFCrewDuties.Guard
                || xform.GridUid is not { } grid || (crew.Post?.EntityId ?? grid) != grid
                || _weapons.HasLiveThreat(uid) || shelter.IsSheltering(uid))
                continue;

            if (!FindPlantWork(grid, uid, out var machine, out var fuel))
                continue;

            // With fuel to fetch the job starts at the stores; a plant that only wants switching on is walked to at once.
            var home = crew.Post ?? xform.Coordinates;
            _jobs[uid] = new Job(grid, crew.Group, WFCrewObjectiveKind.Resupply, fuel ?? machine, home, null, _timing.CurTime)
            {
                Deliver = machine,
                Returning = fuel == null,
            };
        }
    }

    /// <summary>The first machine of the ship's plant that wants fuel or switching on, with the fuel for it if any.</summary>
    private bool FindPlantWork(EntityUid grid, EntityUid engineer, out EntityUid machine, out EntityUid? fuel)
    {
        machine = default;
        fuel = null;
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out var generator, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || Tended(uid))
                continue;

            var left = _generators.GetFuel(uid);
            if (left < LowFuel && FindFuel(grid, uid, engineer) is { } item)
            {
                machine = uid;
                fuel = item;
                return true;
            }

            if (left > 0f && !generator.On)
            {
                machine = uid;
                return true;
            }
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || Tended(uid))
                continue;

            var charge = Antimatter(controller);
            if (charge < LowAntimatter && FindJar(grid, engineer) is { } fresh)
            {
                machine = uid;
                fuel = fresh;
                return true;
            }

            if (charge > 0 && !controller.Injecting)
            {
                machine = uid;
                return true;
            }
        }

        return false;
    }

    private int Antimatter(AmeControllerComponent controller)
    {
        return controller.FuelSlot.Item is { } jar && TryComp<AmeFuelContainerComponent>(jar, out var contents) ? contents.FuelAmount : 0;
    }

    /// <summary>Whether an engineer is already on his way to a machine.</summary>
    private bool Tended(EntityUid machine)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.Deliver == machine)
                return true;
        }

        return false;
    }

    /// <summary>Fuel aboard that a generator burns: a stack of its material, or a can of its reagent.</summary>
    private EntityUid? FindFuel(EntityUid grid, EntityUid generator, EntityUid engineer)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(generator, out var solid))
        {
            var stacks = EntityQueryEnumerator<PhysicalCompositionComponent, StackComponent, TransformComponent>();
            while (stacks.MoveNext(out var uid, out var composition, out _, out var xform))
            {
                if (xform.GridUid == grid && composition.MaterialComposition.ContainsKey(solid.FuelMaterial) && Loose(uid, engineer))
                    return uid;
            }

            return null;
        }

        if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(generator, out var chemical))
            return null;

        var cans = EntityQueryEnumerator<DrainableSolutionComponent, TransformComponent>();
        while (cans.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !Loose(uid, engineer) || !_solutions.TryGetDrainableSolution(uid, out _, out var solution))
                continue;

            foreach (var reagent in chemical.Reagents.Keys)
            {
                if (solution.GetTotalPrototypeQuantity(reagent.Id) > 0)
                    return uid;
            }
        }

        return null;
    }

    private EntityUid? FindJar(EntityUid grid, EntityUid engineer)
    {
        var jars = EntityQueryEnumerator<AmeFuelContainerComponent, TransformComponent>();
        while (jars.MoveNext(out var uid, out var jar, out var xform))
        {
            if (xform.GridUid == grid && jar.FuelAmount >= LowAntimatter && Loose(uid, engineer))
                return uid;
        }

        return null;
    }

    /// <summary>Whether an item is there for the taking: lying about or in a crate, not in a machine or somebody else's hands.</summary>
    private bool Loose(EntityUid item, EntityUid engineer)
    {
        if (Claimed(item))
            return false;
        if (!_containers.TryGetContainingContainer(item, out var container))
            return true;

        var owner = container.Owner;
        return owner == engineer
            || !HasComp<MobStateComponent>(owner) && !HasComp<MaterialStorageComponent>(owner) && !HasComp<AmeControllerComponent>(owner);
    }

    /// <summary>
    /// The engineer at the machine: feeds it what he carries, swapping out the reactor's spent jar first, then starts it.
    /// </summary>
    private bool Tend(EntityUid mob, Job job, EntityUid machine)
    {
        if (TerminatingOrDeleted(machine))
        {
            _jobs.Remove(mob);
            return true;
        }

        if (job.Target != machine)
        {
            if (!_hands.IsHolding(mob, job.Target, out _))
            {
                Fail(job, false);
                return true;
            }

            if (TryComp<AmeControllerComponent>(machine, out var controller))
            {
                if (controller.Injecting)
                    _ame.SetInjecting(machine, false, mob, controller);
                if (controller.FuelSlot.Item != null)
                    _slots.TryEject(machine, controller.FuelSlot, mob, out _);
            }

            _hands.TrySelect(mob, job.Target);
            _interaction.InteractUsing(mob, job.Target, machine, Transform(machine).Coordinates);
        }

        Start(machine, mob);
        _jobs.Remove(mob);
        return true;
    }

    /// <summary>Switches on a generator with fuel in it, or sets a fuelled reactor injecting at the textbook rate.</summary>
    private void Start(EntityUid machine, EntityUid? user)
    {
        if (TryComp<FuelGeneratorComponent>(machine, out var generator))
        {
            if (!generator.On && _generators.GetFuel(machine) > 0f)
                _generators.SetFuelGeneratorOn(machine, true, generator);
            return;
        }

        if (!TryComp<AmeControllerComponent>(machine, out var controller) || Antimatter(controller) <= 0)
            return;

        var grid = Transform(machine).GridUid;
        var cores = 0;
        var shields = EntityQueryEnumerator<AmeShieldComponent, TransformComponent>();
        while (shields.MoveNext(out _, out var shield, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && shield.IsCore)
                cores++;
        }

        // Two a core is the rate the reactor is built for; past four it starts to come apart.
        if (!controller.Injecting)
        {
            _ame.SetInjectionAmount(machine, Math.Max(2, cores * 2), user, controller);
            _ame.SetInjecting(machine, true, user, controller);
        }
    }

    /// <summary>
    /// Tops up a plant that has run low with no engineer to do it: a fighter's crew see to their own generator between
    /// sorties. Generators are filled and lit, a spent reactor jar is swapped for a fresh one.
    /// </summary>
    public void Refuel(EntityUid grid)
    {
        foreach (var machine in Plant(grid))
        {
            if (TryComp<FuelGeneratorComponent>(machine, out _))
            {
                if (_generators.GetFuel(machine) < LowFuel)
                    Fill(machine);
            }
            else if (TryComp<AmeControllerComponent>(machine, out var controller) && Antimatter(controller) < LowAntimatter)
            {
                if (controller.Injecting)
                    _ame.SetInjecting(machine, false, null, controller);
                if (controller.FuelSlot.Item is { } spent && _slots.TryEject(machine, controller.FuelSlot, null, out _))
                    QueueDel(spent);
                var jar = Spawn(Jar, Transform(machine).Coordinates);
                if (!_slots.TryInsert(machine, controller.FuelSlot, jar, null))
                    QueueDel(jar);
            }

            Start(machine, null);
        }
    }

    /// <summary>Fills a generator with what it burns.</summary>
    private void Fill(EntityUid generator)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(generator, out var solid) && TryComp<MaterialStorageComponent>(generator, out var storage)
            && storage.StorageLimit is { } limit)
        {
            var room = limit - _materials.GetMaterialAmount(generator, solid.FuelMaterial, storage);
            if (room > 0)
                _materials.TryChangeMaterialAmount(generator, solid.FuelMaterial, room, storage);
        }
        else if (TryComp<ChemicalFuelGeneratorAdapterComponent>(generator, out var chemical)
            && _solutions.ResolveSolution(generator, chemical.SolutionName, ref chemical.Solution, out var solution))
        {
            foreach (var reagent in chemical.Reagents.Keys)
            {
                _solutions.TryAddReagent(chemical.Solution.Value, reagent.Id, solution.AvailableVolume);
                break;
            }
        }
    }

    /// <summary>Whether a grid has a plant an engineer could tend: a fuel generator or a reactor.</summary>
    public bool HasPlant(EntityUid grid) => Plant(grid).Count > 0;

    private List<EntityUid> Plant(EntityUid grid)
    {
        var plant = new List<EntityUid>();
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                plant.Add(uid);
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                plant.Add(uid);
        }

        return plant;
    }

    /// <summary>The nearest safe tile to the plant, within a few tiles of it, for the engineer's post.</summary>
    public bool TryFindPost(EntityUid grid, out EntityCoordinates post)
    {
        post = default;
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var plant = Plant(grid);
        for (var ring = 1; ring <= PostReach; ring++)
        {
            foreach (var machine in plant)
            {
                var tile = _maps.TileIndicesFor(grid, gridComp, Transform(machine).Coordinates);
                for (var dx = -ring; dx <= ring; dx++)
                {
                    for (var dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || !_planner.IsSafePost(grid, tile + new Vector2i(dx, dy), gridComp))
                            continue;

                        post = _maps.GridTileToLocal(grid, gridComp, tile + new Vector2i(dx, dy));
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>How many tiles from the plant the engineer's post may be.</summary>
    private const int PostReach = 4;

    /// <summary>
    /// Puts a ship's plant in running order, as a crew that has been flying her would have: generators full and lit,
    /// the reactor jarred and injecting. For hulls that come off the slip empty.
    /// </summary>
    public void Commission(EntityUid grid)
    {
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            Fill(uid);
            Start(uid, null);
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            if (controller.FuelSlot.Item == null)
            {
                var jar = Spawn(Jar, xform.Coordinates);
                if (!_slots.TryInsert(uid, controller.FuelSlot, jar, null))
                    QueueDel(jar);
            }

            Start(uid, null);
        }
    }

    /// <summary>
    /// Stows fuel for the trip in a crate in the hold: stacks of each generator's material, a can for a chemical
    /// generator, jars for a reactor. What the engineer feeds the plant from, and what a boarder can carry off.
    /// </summary>
    public bool StockFuel(EntityUid grid)
    {
        var tiles = _planner.HoldTiles(grid, 1);
        if (tiles.Count == 0)
            return false;

        var crate = Spawn(StoresCrate, tiles[0]);
        var stocked = 0;
        foreach (var machine in Plant(grid))
        {
            if (TryComp<SolidFuelGeneratorAdapterComponent>(machine, out var solid))
            {
                if (!_prototypes.TryIndex<MaterialPrototype>(solid.FuelMaterial, out var material) || material.StackEntity is not { } stackId)
                    continue;

                for (var i = 0; i < StoresPerMachine; i++)
                {
                    var stack = Spawn(stackId, tiles[0]);
                    _stacks.SetCount(stack, StackSize);
                    Stow(stack, crate);
                    stocked++;
                }
            }
            else if (HasComp<ChemicalFuelGeneratorAdapterComponent>(machine))
            {
                Stow(Spawn(WeldingFuelCan, tiles[0]), crate);
                stocked++;
            }
            else if (HasComp<AmeControllerComponent>(machine))
            {
                for (var i = 0; i < StoresPerMachine; i++)
                {
                    Stow(Spawn(Jar, tiles[0]), crate);
                    stocked++;
                }
            }
        }

        if (stocked > 0)
            return true;

        QueueDel(crate);
        return false;
    }

    /// <summary>Puts an item in the stores crate; one that won't go in is left beside it.</summary>
    private void Stow(EntityUid item, EntityUid crate)
    {
        _storage.Insert(item, crate);
    }
}
