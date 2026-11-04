using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Adds a storage module's grid to the suit, upgrading the built-in satchel when one exists.</summary>
public sealed partial class ModStorageModuleSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModStorageModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModStorageModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }
    private void OnActivated(Entity<ModStorageModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (ent.Comp.Upgraded)
            return;

        if (!ent.Comp.Components.TryGetComponent("Storage", out var configured)
            || configured is not StorageComponent moduleStorage)
            return;

        ent.Comp.Upgraded = true;

        if (TryComp<StorageComponent>(args.Suit, out var storage))
        {
            ent.Comp.SavedGrid = storage.Grid;
            storage.Grid = moduleStorage.Grid;
            Dirty(args.Suit, storage);
            return;
        }

        EntityManager.AddComponents(args.Suit, ent.Comp.Components);
    }
    private void OnDeactivated(Entity<ModStorageModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (!ent.Comp.Upgraded)
            return;

        ent.Comp.Upgraded = false;

        if (!TryComp<StorageComponent>(args.Suit, out var storage))
            return;

        if (ent.Comp.SavedGrid is { } grid)
        {
            storage.Grid = grid;
            Dirty(args.Suit, storage);
            return;
        }

        var coords = _transform.GetMoverCoordinates(args.Suit);
        _container.EmptyContainer(storage.Container, true, coords);
        RemComp(args.Suit, storage);
    }
}
