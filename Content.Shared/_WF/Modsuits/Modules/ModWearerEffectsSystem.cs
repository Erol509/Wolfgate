using Content.Shared.Actions.Events;
using Content.Shared.Gravity;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Marks a wearer protected from shoves by the bulwark module.</summary>
[RegisterComponent]
public sealed partial class ModShoveBlockerActiveComponent : Component;

/// <summary>Wearer-side effects of modules that only modify the person inside the suit.</summary>
public sealed partial class ModWearerEffectsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModShoveBlockerComponent, ModModuleActivatedEvent>(OnShoveBlockerActivated);
        SubscribeLocalEvent<ModShoveBlockerComponent, ModModuleDeactivatedEvent>(OnShoveBlockerDeactivated);
        SubscribeLocalEvent<ModShoveBlockerActiveComponent, DisarmAttemptEvent>(OnDisarmAttempt);
    }

    [Dependency] private SharedGravitySystem _gravity = default!;
    private void OnShoveBlockerActivated(Entity<ModShoveBlockerComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            EnsureComp<ModShoveBlockerActiveComponent>(wearer);
    }
    private void OnShoveBlockerDeactivated(Entity<ModShoveBlockerComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemComp<ModShoveBlockerActiveComponent>(wearer);
    }
    private void OnDisarmAttempt(Entity<ModShoveBlockerActiveComponent> ent, ref DisarmAttemptEvent args)
    {
        args.Cancel();
    }
}
