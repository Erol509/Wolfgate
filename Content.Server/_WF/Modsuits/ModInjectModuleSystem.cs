using Content.Server.Body.Systems;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Modules;
using Content.Shared.Stunnable;
using Content.Shared.Standing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Modsuits;

/// <summary>Applies the injector module's reagent payload on the server.</summary>
public sealed partial class ModInjectModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ModInjectModuleComponent, ModModuleUsedEvent>(OnUsed);
    }

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    private void OnUsed(Entity<ModInjectModuleComponent> ent, ref ModModuleUsedEvent args)
    {
        args.Handled = true;
        _audio.PlayPredicted(ent.Comp.Sound, args.User, args.User);

        if (ent.Comp.ClearStuns)
        {
            RemComp<StunnedComponent>(args.User);
            RemComp<KnockedDownComponent>(args.User);
            _standing.Stand(args.User, force: true);
        }

        _bloodstream.TryAddToChemicals(args.User, ent.Comp.Solution.Clone());
    }
}
