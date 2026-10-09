using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.FtlEffects;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.FtlEffects;

/// <summary>Publishes departure visuals for normal, docking and administrative FTL jumps.</summary>
public sealed partial class FtlDepartureSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(ShuttleSystem));
        SubscribeLocalEvent<FTLComponent, FTLStartedEvent>(OnStarted);
        SubscribeLocalEvent<MapGridComponent, FTLCompletedEvent>(OnCompleted);
    }

    private void OnStarted(Entity<FTLComponent> ent, ref FTLStartedEvent args)
    {
        var effect = EnsureComp<FtlDepartureComponent>(ent);
        effect.Started = _timing.CurTime - TimeSpan.FromSeconds(ent.Comp.StartupTime);
        effect.Departure = _timing.CurTime;
        effect.Entered = true;
        effect.Arriving = false;
        Dirty(ent, effect);
    }

    private void OnCompleted(Entity<MapGridComponent> ent, ref FTLCompletedEvent args)
    {
        var effect = EnsureComp<FtlDepartureComponent>(ent);
        effect.Started = _timing.CurTime;
        effect.Departure = _timing.CurTime;
        effect.Entered = true;
        effect.Arriving = true;
        Dirty(ent, effect);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var departing = EntityQueryEnumerator<FTLComponent, MapGridComponent>();
        while (departing.MoveNext(out var uid, out var ftl, out _))
        {
            if (ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
                continue;

            var effect = EnsureComp<FtlDepartureComponent>(uid);
            if (effect.Started == ftl.StateTime.Start && effect.Departure == ftl.StateTime.End && !effect.Entered)
                continue;

            effect.Started = ftl.StateTime.Start;
            effect.Departure = ftl.StateTime.End;
            effect.Entered = false;
            effect.Arriving = false;
            Dirty(uid, effect);
        }

        var effects = EntityQueryEnumerator<FtlDepartureComponent>();
        while (effects.MoveNext(out var uid, out var effect))
        {
            if (effect.Arriving)
            {
                if (now >= effect.Departure + TimeSpan.FromSeconds(FtlDepartureTiming.FadeDuration))
                    RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            if (!TryComp<FTLComponent>(uid, out var ftl) ||
                ftl.State is not (FTLState.Starting or FTLState.Travelling or FTLState.Arriving))
            {
                RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            if (ftl.State == FTLState.Starting)
                continue;

            if (!effect.Entered)
            {
                effect.Entered = true;
                effect.Departure = now;
                Dirty(uid, effect);
            }

            if (now >= effect.Departure + TimeSpan.FromSeconds(FtlDepartureTiming.FadeDuration))
                RemCompDeferred<FtlDepartureComponent>(uid);
        }
    }
}
