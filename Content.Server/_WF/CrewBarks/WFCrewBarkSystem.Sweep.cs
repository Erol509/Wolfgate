using System.Linq;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.ShipCommand;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.CrewBarks;

public sealed partial class WFCrewBarkSystem
{
    private float _timer;

    /// <summary>Players aboard each barking ship, gathered once per sweep.</summary>
    private readonly Dictionary<EntityUid, List<EntityUid>> _aboard = new();

    private readonly List<EntityUid> _none = new();
    private readonly List<(EntityCoordinates, TimeSpan)> _keptRecent = new();
    private readonly List<EntityUid> _forgotten = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;

        _timer = 0f;
        Sweep();
    }

    /// <summary>Once a second: reports at the post, greetings for whoever passes, and now and then a thought.</summary>
    private void Sweep()
    {
        var now = _timing.CurTime;
        ForgetRecent(now);
        GatherAboard();

        var query = EntityQueryEnumerator<WFCrewBarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var barker, out var xform))
        {
            if (xform.GridUid != barker.Grid || !TryComp<WFCrewBarksComponent>(barker.Grid, out var barks)
                || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;

            ForgetGreetings(barker, barks, now);
            var aboard = _aboard.GetValueOrDefault(barker.Grid) ?? _none;
            var mob = new Entity<WFCrewBarkerComponent>(uid, barker);
            if (barker.Arriving)
                Arrive(mob, barks, xform, aboard);

            if (Busy(uid))
                continue;

            Greet(mob, barks, xform, aboard, now);
            if (now >= barker.NextIdle)
            {
                barker.NextIdle = now + IdleGap(barks);
                if (Near(xform, aboard, barks.HearRange))
                    Bark(mob.AsNullable(), WFCrewBark.Idle);
            }
        }
    }

    private void GatherAboard()
    {
        _aboard.Clear();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !HasComp<WFCrewBarksComponent>(grid) || !_mobs.IsAlive(uid))
                continue;

            if (!_aboard.TryGetValue(grid, out var list))
                _aboard[grid] = list = new List<EntityUid>();
            list.Add(uid);
        }
    }

    private void ForgetRecent(TimeSpan now)
    {
        _keptRecent.Clear();
        foreach (var entry in _recent)
        {
            if (now < entry.Until)
                _keptRecent.Add(entry);
        }
        _recent.Clear();
        _recent.AddRange(_keptRecent);
    }

    private void ForgetGreetings(WFCrewBarkerComponent barker, WFCrewBarksComponent barks, TimeSpan now)
    {
        if (barker.Greeted.Count == 0)
            return;

        _forgotten.Clear();
        foreach (var (person, at) in barker.Greeted)
        {
            if (now >= at + barks.GreetAgain || TerminatingOrDeleted(person))
                _forgotten.Add(person);
        }
        foreach (var person in _forgotten)
            barker.Greeted.Remove(person);
    }

    /// <summary>In a fight of his own: no greetings, no idle talk.</summary>
    private bool Busy(EntityUid uid)
    {
        return TryComp<FactionExceptionComponent>(uid, out var exceptions) && exceptions.Hostiles.Count > 0;
    }

    private bool Near(TransformComponent xform, List<EntityUid> aboard, float range)
    {
        foreach (var person in aboard)
        {
            if (_transform.InRange(xform.Coordinates, Transform(person).Coordinates, range))
                return true;
        }
        return false;
    }

    /// <summary>A crewman back from the crew bay reports once he stands at his post, if anyone is there to hear it.</summary>
    private void Arrive(Entity<WFCrewBarkerComponent> mob, WFCrewBarksComponent barks, TransformComponent xform,
        List<EntityUid> aboard)
    {
        if (!TryComp<WFCrewComponent>(mob, out var crew) || crew.Post is not { } post)
        {
            mob.Comp.Arriving = false;
            return;
        }

        if (!_transform.InRange(xform.Coordinates, post, crew.PostRange + 0.5f))
            return;

        if (!Near(xform, aboard, barks.HearRange) || Bark(mob.AsNullable(), WFCrewBark.Reporting))
            mob.Comp.Arriving = false;
    }

    /// <summary>
    /// Greets one person passing close by and in sight whom he hasn't greeted lately: the Boss with a salute, the
    /// staff warmly, a guest only sometimes, the barred curtly. Nobody he is fighting or turning away.
    /// </summary>
    private void Greet(Entity<WFCrewBarkerComponent> mob, WFCrewBarksComponent barks, TransformComponent xform,
        List<EntityUid> aboard, TimeSpan now)
    {
        foreach (var person in aboard)
        {
            if (person == mob.Owner
                || mob.Comp.Greeted.TryGetValue(person, out var at) && now < at + barks.GreetAgain
                || !_transform.InRange(xform.Coordinates, Transform(person).Coordinates, barks.GreetRange)
                || !_weapons.CanSee(mob, person))
                continue;

            if (Greeting(mob, person) is not { } kind
                || kind == WFCrewBark.GreetGuest && !_random.Prob(barks.GuestGreetChance))
            {
                mob.Comp.Greeted[person] = now;
                continue;
            }

            if (!CanBark(mob, kind))
                return;

            if (kind == WFCrewBark.GreetBoss)
                _chat.TryEmoteWithChat(mob, "Salute");
            if (Bark(mob.AsNullable(), kind, ("name", Identity.Name(person, EntityManager, mob.Owner))))
                mob.Comp.Greeted[person] = now;
            return;
        }
    }

    /// <summary>How a barker greets someone, or null when he won't: someone he fights, or a crewman's unwelcome visitor.</summary>
    private WFCrewBark? Greeting(Entity<WFCrewBarkerComponent> mob, EntityUid person)
    {
        if (TryComp<FactionExceptionComponent>(mob, out var exceptions)
            && _factions.GetHostiles((mob, exceptions)).Contains(person))
            return null;

        var authority = Standing(mob.Comp.Grid, person, out var boss);
        if (authority == WFShipAuthority.Barred)
            return WFCrewBark.GreetBarred;

        // The crew's warnings and fights answer someone where he may not be; counter staff don't keep the zones.
        if (HasComp<WFCrewComponent>(mob) && !_security.IsAuthorized(mob, person))
            return null;

        if (boss)
            return WFCrewBark.GreetBoss;
        return authority == WFShipAuthority.Staff ? WFCrewBark.GreetStaff : WFCrewBark.GreetGuest;
    }
}
