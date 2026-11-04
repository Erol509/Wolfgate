using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Interaction;

namespace Content.Shared._WF\Modsuits\Modules;

/// <summary>Marks a module that lets its wearer emag machinery by touch.</summary>
[RegisterComponent]
public sealed partial class ModHackerModuleComponent : Component;

/// <summary>Marks a wearer whose active hacker module can emag machinery.</summary>
[RegisterComponent]
public sealed partial class ModHackerActiveComponent : Component
{
    [DataField]
    public EntityUid Module;
}

/// <summary>Routes the hacker module's touch interaction through the existing emag system.</summary>
public sealed partial class ModHackerModuleSystem : EntitySystem
{
    [Dependency] private EmagSystem _emag = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModHackerModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModHackerModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModHackerActiveComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnActivated(Entity<ModHackerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is not { } wearer)
            return;

        var active = EnsureComp<ModHackerActiveComponent>(wearer);
        active.Module = ent;
        Dirty(wearer, active);
    }

    private void OnDeactivated(Entity<ModHackerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemComp<ModHackerActiveComponent>(wearer);
    }

    private void OnAfterInteract(Entity<ModHackerActiveComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target
            || !TryComp<EmagComponent>(ent, out var emag))
            return;

        args.Handled = _emag.TryEmagEffect((ent.Owner, emag), args.User, target);
    }
}
