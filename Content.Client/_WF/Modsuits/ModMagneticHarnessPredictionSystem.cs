using Content.Shared._WF.Modsuits.Modules;
using Robust.Client.Physics;
using Robust.Client.Player;

namespace Content.Client._WF.Modsuits;

public sealed partial class ModMagneticHarnessPredictionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModMagnetizedGunComponent, UpdateIsPredictedEvent>(OnUpdateIsPredicted);
        SubscribeLocalEvent<ModMagnetizedGunComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private Robust.Client.Physics.PhysicsSystem _physics = default!;
    private void OnUpdateIsPredicted(Entity<ModMagnetizedGunComponent> ent, ref UpdateIsPredictedEvent args)
    {
        if (ent.Comp.LooseSince != null && ent.Comp.Wearer == _player.LocalEntity)
            args.IsPredicted = true;
    }
    private void OnAfterState(Entity<ModMagnetizedGunComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _physics.UpdateIsPredicted(ent);
    }
}
