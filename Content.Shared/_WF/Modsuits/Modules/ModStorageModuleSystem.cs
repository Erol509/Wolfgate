using Content.Shared.Storage;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Gives the suit storage while the module is active; removing it drops the contents and takes the storage away.</summary>
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
        if (ent.Comp.Upgraded || !ent.Comp.Components.TryGetComponent("Storage", out _))
            return;

        ent.Comp.Upgraded = true;
        EntityManager.AddComponents(args.Suit, ent.Comp.Components);
    }
    private void OnDeactivated(Entity<ModStorageModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (!ent.Comp.Upgraded)
            return;

        ent.Comp.Upgraded = false;

        if (!TryComp<StorageComponent>(args.Suit, out var storage))
            return;

        var coords = _transform.GetMoverCoordinates(args.Suit);
        _container.EmptyContainer(storage.Container, true, coords);
        RemComp(args.Suit, storage);
    }
}
