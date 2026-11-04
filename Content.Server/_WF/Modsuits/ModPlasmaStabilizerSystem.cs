using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Modules;

namespace Content.Server._WF.Modsuits;

/// <summary>
/// Plasma stabilizer module. Wolfgate has no gas-ignition system for plasmamen, so the module is
/// present but inert; see the module checklist in the README.
/// </summary>
public sealed partial class ModPlasmaStabilizerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModPlasmaStabilizerModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModPlasmaStabilizerModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
    }
    private void OnActivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
    }
    private void OnDeactivated(Entity<ModPlasmaStabilizerModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
    }
}
