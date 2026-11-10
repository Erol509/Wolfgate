using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModSoundModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSoundModuleComponent, ModModuleUsedEvent>(OnSoundUsed);
        SubscribeLocalEvent<ModSpawnModuleComponent, ModModuleUsedEvent>(OnSpawnUsed);
    }

    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    private void OnSoundUsed(Entity<ModSoundModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        _audio.PlayPredicted(ent.Comp.Sound, args.User, args.User);
        args.Handled = true;
    }
    private void OnSpawnUsed(Entity<ModSpawnModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        if (_net.IsServer)
            Spawn(ent.Comp.Prototype, Transform(args.User).Coordinates);

        args.Handled = true;
    }
}
