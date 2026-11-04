using Content.Shared._CE.ZLevels.Damage;

namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Marks a wearer protected from z-level fall damage by the longfall module.</summary>
[RegisterComponent]
public sealed partial class ModLongfallActiveComponent : Component;

/// <summary>Translates the longfall module onto the z-level fall damage system.</summary>
public sealed partial class ModLongfallModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModLongfallModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModLongfallModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModLongfallActiveComponent, CEZFallingDamageCalculateEvent>(OnFallingDamage);
    }
    private void OnActivated(Entity<ModLongfallModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            EnsureComp<ModLongfallActiveComponent>(wearer);
    }
    private void OnDeactivated(Entity<ModLongfallModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            RemComp<ModLongfallActiveComponent>(wearer);
    }
    private void OnFallingDamage(Entity<ModLongfallActiveComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        args.DamageMultiplier = 0f;
        args.StunMultiplier = 0f;
    }
}
