using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Cloning.Components;
using Content.Server.Power.Components;
using Content.Server._Mono.Cloning;
using Content.Server._WF.NpcCrew.Components;
using Content.Shared._Mono.Cloning;
using Content.Shared.Chat;
using Content.Shared.Cloning;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// M4: the surgeon clones from the ship's genetic records. A living player can ask him, by a verb on him, to take
/// their record; he writes it to the genetic database on his home grid, and the same verb withdraws it, deleting the
/// record. Records made at the body scanner count the same (the v0.9 review). When a player on file dies he clones them
/// in a free cloning pod aboard: at once if the body is gone or can't be revived, otherwise once
/// <see cref="WFCrewMedicComponent.CloneAfter"/> has passed with nobody bringing it back. The cloner still asks the
/// player before they wake in the new body; a clone they turn down or leave unanswered is tried once more after
/// <see cref="CloneRetryGap"/>, then not again until they are alive once more.
/// </summary>
public sealed partial class WFCrewMedicSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private GeneticCloningSystem _geneticCloning = default!;
    [Dependency] private GeneticDatabaseSystem _database = default!;

    private static readonly TimeSpan CloneCheckGap = TimeSpan.FromSeconds(5);

    /// <summary>How long after a clone the player didn't take before the surgeon tries them once more.</summary>
    private static readonly TimeSpan CloneRetryGap = TimeSpan.FromMinutes(10);

    /// <summary>Clones started for one death before the surgeon stops: the first, and one more after the gap.</summary>
    private const int MaxCloneTries = 2;

    /// <summary>When each mind on file was first seen cloneable, for the wait on a revivable body.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _deadSince = new();

    /// <summary>Clones started for each mind on file since it was last alive, and when another may be tried.</summary>
    private readonly Dictionary<EntityUid, (int Tries, TimeSpan Next)> _cloneTries = new();

    private void InitializeCloning()
    {
        SubscribeLocalEvent<WFCrewMedicComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<WFCrewMedicComponent> medic, ref GetVerbsEvent<Verb> args)
    {
        if (medic.Comp.Rank != WFCrewMedicRank.Surgeon || !args.CanAccess || !args.CanInteract || args.User == medic.Owner
            || !_mobState.IsAlive(medic) || !_mobState.IsAlive(args.User)
            || Database(medic) is not { } db || !_mind.TryGetMind(args.User, out var mindId, out _))
            return;

        var user = args.User;
        var onFile = OnFile(db, mindId);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(onFile ? "wf-crew-surgeon-verb-withdraw" : "wf-crew-surgeon-verb-register"),
            Message = Loc.GetString(onFile ? "wf-crew-surgeon-verb-withdraw-message" : "wf-crew-surgeon-verb-register-message"),
            Act = () =>
            {
                if (onFile)
                    Withdraw(medic, user);
                else
                    TryRegister(medic, user, out _);
            },
            Priority = -1,
        });
    }

    /// <summary>
    /// Takes a living player's genetic record into the ship's database. False, with the reason, when he can't: no
    /// database aboard, a body the scanner would refuse, or someone the ship's medics won't treat.
    /// </summary>
    public bool TryRegister(Entity<WFCrewMedicComponent> medic, EntityUid user, out string? reason)
    {
        reason = null;
        if (medic.Comp.Rank != WFCrewMedicRank.Surgeon || !_mobState.IsAlive(user) || Database(medic) is not { } db)
        {
            reason = Loc.GetString("wf-crew-surgeon-register-no-database");
            SayLine(medic, reason);
            return false;
        }

        if (TryComp<WFCrewComponent>(medic, out var crew) && _crew.HomeGrid(medic, crew) is { } home && !MayTreat(medic, user, home))
        {
            reason = Loc.GetString("wf-crew-surgeon-register-refused");
            SayLine(medic, reason);
            return false;
        }

        if (_geneticCloning.CreateRecord(user, out var fail) is not { } record)
        {
            reason = fail ?? Loc.GetString("wf-crew-surgeon-register-refused");
            SayLine(medic, reason);
            return false;
        }

        _database.Upsert(db, record);
        SayLine(medic, Loc.GetString("wf-crew-surgeon-registered", ("name", record.Name)));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} had {ToPrettyString(medic.Owner):medic} take their genetic record into {ToPrettyString(db.Owner):database}");

        var ev = new WFCrewTreatedEvent(medic, user, WFCrewMedicAction.Register);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Deletes a player's record from the database, at their asking.</summary>
    public void Withdraw(Entity<WFCrewMedicComponent> medic, EntityUid user)
    {
        if (Database(medic) is not { } db || !_mind.TryGetMind(user, out var mindId, out _))
            return;

        if (db.Comp.Records.TryGetValue(mindId, out var record))
            _database.Remove(db, record);
        _deadSince.Remove(mindId);
        _cloneTries.Remove(mindId);

        SayLine(medic, Loc.GetString("wf-crew-surgeon-withdrawn"));
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} had {ToPrettyString(medic.Owner):medic} delete their genetic record from {ToPrettyString(db.Owner):database}");
    }

    /// <summary>True when this mind has a record in the database.</summary>
    public bool OnFile(Entity<GeneticDatabaseComponent> db, EntityUid mind)
    {
        return db.Comp.Records.ContainsKey(mind);
    }

    /// <summary>
    /// Every few seconds: the first player on file who is dead and ready to clone is cloned in a free pod aboard. One at
    /// a time.
    /// </summary>
    public void TickCloning(Entity<WFCrewMedicComponent> medic, WFCrewComponent crew)
    {
        var now = _timing.CurTime;
        if (now < medic.Comp.NextCloneCheck)
            return;

        medic.Comp.NextCloneCheck = now + CloneCheckGap;
        PruneCloning();
        if (Database(medic) is not { } db)
            return;

        foreach (var (mindId, record) in db.Comp.Records.ToArray())
        {
            var status = _database.GetStatus(record);
            // Back in a body: a later death is a new one, with its own tries.
            if (status == GeneticRecordStatus.Alive)
                _cloneTries.Remove(mindId);

            if (status != GeneticRecordStatus.Cloneable || !TryComp<MindComponent>(mindId, out var mind)
                || _geneticCloning.IsBeingCloned(mind))
            {
                _deadSince.Remove(mindId);
                continue;
            }

            if (CloneRefused(medic, crew, mind))
            {
                _deadSince.Remove(mindId);
                continue;
            }

            // Still cloneable after a clone was made: the player turned it down or let it lie. Not pressed on them.
            _cloneTries.TryGetValue(mindId, out var tried);
            if (tried.Tries >= MaxCloneTries || now < tried.Next)
                continue;

            if (!_deadSince.TryGetValue(mindId, out var since))
                _deadSince[mindId] = since = now;

            if (!ReadyToClone(medic, mind, now - since))
                continue;

            if (FreeCloningPod(medic) is not { } pod)
                return;

            if (!_geneticCloning.TryCloning(pod, record, pod.Comp))
                continue;

            _deadSince.Remove(mindId);
            _cloneTries[mindId] = (tried.Tries + 1, now + CloneRetryGap);
            var clone = pod.Comp.BodyContainer.ContainedEntity ?? pod.Owner;
            medic.Comp.Last = WFCrewMedicAction.Clone;
            SayLine(medic, Loc.GetString("wf-crew-surgeon-cloning", ("name", record.Name)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(medic.Owner):medic} cloned {record.Name} ({ToPrettyString(mindId)}) from {ToPrettyString(db.Owner):database} in {ToPrettyString(pod.Owner):pod}");

            var ev = new WFCrewTreatedEvent(medic, clone, WFCrewMedicAction.Clone);
            RaiseLocalEvent(ref ev);
            return;
        }
    }

    /// <summary>Forgets the waits and clone tries of minds that are gone.</summary>
    private void PruneCloning()
    {
        foreach (var mind in _cloneTries.Keys.ToArray())
        {
            if (TerminatingOrDeleted(mind))
                _cloneTries.Remove(mind);
        }

        foreach (var mind in _deadSince.Keys.ToArray())
        {
            if (TerminatingOrDeleted(mind))
                _deadSince.Remove(mind);
        }
    }

    /// <summary>Whether the ship has refused this person since they registered: barred, or an attacker of its crew.</summary>
    private bool CloneRefused(Entity<WFCrewMedicComponent> medic, WFCrewComponent crew, MindComponent mind)
    {
        if (_crew.HomeGrid(medic, crew) is not { } home)
            return false;

        var ev = new WFCrewMayTreatEvent(medic, mind.OwnedEntity ?? EntityUid.Invalid) { PatientUser = mind.UserId };
        RaiseLocalEvent(home, ref ev);
        return ev.Refused;
    }

    /// <summary>
    /// A player on file is cloned at once when their body is gone or can't be revived (by a surgeon either), and
    /// otherwise only once the wait is over and no medic is working on the body.
    /// </summary>
    private bool ReadyToClone(Entity<WFCrewMedicComponent> medic, MindComponent mind, TimeSpan deadFor)
    {
        if (mind.OwnedEntity is not { } body || TerminatingOrDeleted(body) || !HasComp<MobStateComponent>(body))
            return true;

        if (!_mobState.IsDead(body))
            return false;

        if (_revival.GetRefusal(body) is { } refusal && !Fixable.Contains(refusal))
            return true;

        return deadFor >= medic.Comp.CloneAfter && !_claims.ContainsKey(body);
    }

    /// <summary>The powered, anchored genetic database on the surgeon's home grid; null when there is none.</summary>
    private Entity<GeneticDatabaseComponent>? Database(Entity<WFCrewMedicComponent> medic)
    {
        if (!TryComp<WFCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var query = EntityQueryEnumerator<GeneticDatabaseComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var db, out var xform))
        {
            if (xform.GridUid == home && xform.Anchored && Powered(uid))
                return (uid, db);
        }

        return null;
    }

    /// <summary>A powered, anchored cloning pod on the surgeon's home grid with nobody in it.</summary>
    private Entity<CloningPodComponent>? FreeCloningPod(Entity<WFCrewMedicComponent> medic)
    {
        if (!TryComp<WFCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var query = EntityQueryEnumerator<CloningPodComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pod, out var xform))
        {
            if (xform.GridUid == home && xform.Anchored && !HasComp<ActiveCloningPodComponent>(uid)
                && pod.BodyContainer.ContainedEntity == null && Powered(uid))
                return (uid, pod);
        }

        return null;
    }

    /// <summary>
    /// Powered, or needing no power. A receiver that needs none counts at once: the power net only refreshes its flag on
    /// its own interval (the autodoc's rule).
    /// </summary>
    private bool Powered(EntityUid uid)
    {
        return !TryComp<ApcPowerReceiverComponent>(uid, out var receiver) || !receiver.NeedsPower || receiver.Powered;
    }

    /// <summary>Says a line aloud now, whatever the chatter gap.</summary>
    private void SayLine(Entity<WFCrewMedicComponent> medic, string line)
    {
        _chat.TrySendInGameICMessage(medic, line, InGameICChatType.Speak, ChatTransmitRange.Normal);
    }
}
