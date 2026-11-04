namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Marks a wearer whose active loudspeaker module widens their speech range.</summary>
[RegisterComponent]
public sealed partial class ModLoudspeakerActiveComponent : Component
{
    [DataField]
    public float Range = 20f;
}

public sealed partial class ModLoudspeakerModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModLoudspeakerModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModLoudspeakerModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }
    private void OnActivated(Entity<ModLoudspeakerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is not { } wearer)
            return;

        ent.Comp.User = wearer;
        var active = EnsureComp<ModLoudspeakerActiveComponent>(wearer);
        active.Range = ent.Comp.Range;
        Dirty(wearer, active);
    }
    private void OnDeactivated(Entity<ModLoudspeakerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        if (ent.Comp.User is not { } wearer)
            return;

        ent.Comp.User = null;
        RemComp<ModLoudspeakerActiveComponent>(wearer);
    }
}
