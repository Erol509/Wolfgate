using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipCommand;
using Content.Server.Shuttles.Events;
using Content.Shared._Onyx.Wounds;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;

namespace Content.Server._WF.CrewBarks;

public sealed partial class WFCrewBarkSystem
{
    private readonly List<Entity<WFCrewBarkerComponent>> _speakers = new();

    private void InitializeReactions()
    {
        // After friendly fire's protection, which may cancel the hit, and before the wounds lose its origin.
        SubscribeLocalEvent<WFCrewBarkerComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            after: [typeof(WFCrewFriendlyFireSystem)], before: [typeof(WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<WFCrewBarkerComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<WFCrewBarkerComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WFCrewBarkerComponent, WFShipPostRelievedEvent>(OnRelieved);
        SubscribeLocalEvent<WFCrewTreatedEvent>(OnTreated);
        SubscribeLocalEvent<WFCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WFCrewBarksComponent, FTLStartedEvent>(OnJump);
        SubscribeLocalEvent<WFCrewBarksComponent, FTLCompletedEvent>(OnArrived);
    }

    private void OnBeforeDamage(Entity<WFCrewBarkerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } origin)
            return;

        Struck(ent, _crew.Wielder(origin));
    }

    private void OnDisarmed(Entity<WFCrewBarkerComponent> ent, ref DisarmedEvent args)
    {
        Struck(ent, args.Source);
    }

    /// <summary>The Boss's blow is taken with thanks (and forgiven by the crew's own rules); anyone else's is not.</summary>
    private void Struck(Entity<WFCrewBarkerComponent> ent, EntityUid attacker)
    {
        if (attacker == ent.Owner || TerminatingOrDeleted(attacker) || !HasComp<MobStateComponent>(attacker)
            || _crew.SameCrew(ent, attacker))
            return;

        Standing(ent.Comp.Grid, attacker, out var boss);
        Bark(ent.AsNullable(), boss ? WFCrewBark.Struck : WFCrewBark.Attacked);
    }

    private void OnDamageChanged(Entity<WFCrewBarkerComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased || args.DamageDelta is not { } delta
            || !TryComp<WFCrewBarksComponent>(ent.Comp.Grid, out var barks)
            || -delta.GetTotal().Float() < barks.HealedAt)
            return;

        Bark(ent.AsNullable(), WFCrewBark.Healed);
    }

    private void OnTreated(ref WFCrewTreatedEvent args)
    {
        if (TryComp<WFCrewBarkerComponent>(args.Patient, out var barker))
            Bark((args.Patient, barker), WFCrewBark.Healed);
    }

    /// <summary>A crewman relieved of his post says his own word in place of the roster's.</summary>
    private void OnRelieved(Entity<WFCrewBarkerComponent> ent, ref WFShipPostRelievedEvent args)
    {
        ent.Comp.Arriving = false;
        var said = args.By is { } by
            ? Bark(ent.AsNullable(), WFCrewBark.Relieved, ("name", Identity.Name(by, EntityManager, ent.Owner)))
            : Bark(ent.AsNullable(), WFCrewBark.StoodDown);
        if (said)
            args.Spoke = true;
    }

    /// <summary>When the crew find a hostile, one of them who sees it calls it out.</summary>
    private void OnAlert(ref WFCrewAlertEvent args)
    {
        if (!HasComp<WFCrewBarksComponent>(args.Grid))
            return;

        var query = EntityQueryEnumerator<WFCrewBarkerComponent, WFCrewComponent>();
        while (query.MoveNext(out var uid, out var barker, out var crew))
        {
            if (barker.Grid != args.Grid || crew.Group != args.Group)
                continue;

            foreach (var hostile in args.Hostiles)
            {
                if (_weapons.CanSee(uid, hostile) && Bark((uid, barker), WFCrewBark.Fight))
                    return;
            }
        }
    }

    private void OnJump(Entity<WFCrewBarksComponent> grid, ref FTLStartedEvent args)
    {
        Announce(grid, WFCrewBark.Jump);
    }

    private void OnArrived(Entity<WFCrewBarksComponent> grid, ref FTLCompletedEvent args)
    {
        Announce(grid, WFCrewBark.Arrived);
    }

    /// <summary>Someone near each player aboard says it; the area gap keeps it to one voice per spot.</summary>
    private void Announce(Entity<WFCrewBarksComponent> grid, WFCrewBark kind)
    {
        GatherAboard();
        if (!_aboard.TryGetValue(grid, out var aboard))
            return;

        _speakers.Clear();
        var query = EntityQueryEnumerator<WFCrewBarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var barker, out var xform))
        {
            if (barker.Grid == grid.Owner && xform.GridUid == grid.Owner && Near(xform, aboard, grid.Comp.HearRange))
                _speakers.Add((uid, barker));
        }

        _random.Shuffle(_speakers);
        foreach (var speaker in _speakers)
            Bark(speaker.AsNullable(), kind);
    }
}
