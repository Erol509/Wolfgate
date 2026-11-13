using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Bed.Sleep;
using Content.Shared.Drunk;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// What a high, a crash, a drink or withdrawal looks like from outside, on examine. A face line needs the eyes
/// uncovered and a close look, and shows from a light high; a body line is how they move, which a sealed suit doesn't
/// hide, and shows from a heavy one at any distance. Each is the strongest thing going on, and none names a drug.
/// </summary>
public sealed class WFDrugCuesSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WFAlcoholSystem _alcohol = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    /// <summary>Withdrawal shows from this stage; the first is only cravings.</summary>
    public const int WithdrawalStage = 2;

    private static readonly WFDrugFamily[] Families =
    {
        WFDrugFamily.Stimulant, WFDrugFamily.Opioid, WFDrugFamily.Psychedelic, WFDrugFamily.Empathogen,
        WFDrugFamily.Cannabinoid, WFDrugFamily.Combat,
    };

    public override void Initialize()
    {
        base.Initialize();
        // One body can carry all three; the first that is there speaks for it.
        SubscribeLocalEvent<WFIntoxicationComponent, ExaminedEvent>((uid, _, args) => OnExamined(uid, args));
        SubscribeLocalEvent<WFDependenceComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<WFIntoxicationComponent>(uid))
                OnExamined(uid, args);
        });
        SubscribeLocalEvent<DrunkComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<WFIntoxicationComponent>(uid) && !HasComp<WFDependenceComponent>(uid))
                OnExamined(uid, args);
        });
    }

    private void OnExamined(EntityUid uid, ExaminedEvent args)
    {
        var (face, body) = GetCues(uid, args.IsInDetailsRange);
        var target = Identity.Entity(uid, EntityManager);
        if (face != null)
            args.PushMarkup(Loc.GetString(face, ("target", target)));
        if (body != null)
            args.PushMarkup(Loc.GetString(body, ("target", target)));
    }

    /// <summary>The face and body lines a body shows, as Fluent IDs; either may be null.</summary>
    public (string? Face, string? Body) GetCues(EntityUid uid, bool detailed)
    {
        if (_mobState.IsDead(uid))
            return (null, null);

        var face = new Cue();
        var body = new Cue();
        var host = _intoxication.IsNeurowareHost(uid);

        if (TryComp<WFIntoxicationComponent>(uid, out var comp))
        {
            foreach (var family in Families)
            {
                var phase = _intoxication.FamilyPhase(comp, family);
                if (phase == WFDrugPhase.None)
                    continue;

                var name = host ? "synth" : family.ToString().ToLowerInvariant();
                face.Offer($"wf-drug-cue-{name}-face", (int) phase * 10);
                if (phase >= WFDrugPhase.Heavy)
                    body.Offer($"wf-drug-cue-{name}-body-{PhaseName(phase)}", (int) phase * 10);
            }

            if (detailed)
            {
                foreach (var _ in _intoxication.ActiveCrashes(comp))
                {
                    body.Offer(host ? "wf-drug-cue-synth-crash" : "wf-drug-cue-crash", 15);
                    break;
                }
            }
        }

        var drunk = _alcohol.DrunkPhase(uid);
        if (drunk != WFDrugPhase.None)
        {
            face.Offer("wf-drug-cue-drunk-face", (int) drunk * 10 - 1);
            if (drunk >= WFDrugPhase.Heavy)
                body.Offer($"wf-drug-cue-drunk-body-{PhaseName(drunk)}", (int) drunk * 10 - 1);
        }

        if (TryComp<WFDependenceComponent>(uid, out var dependence))
        {
            var stage = 0;
            foreach (var reached in dependence.Stage.Values)
            {
                stage = Math.Max(stage, reached);
            }

            if (stage >= WithdrawalStage)
            {
                body.Offer(host ? "wf-drug-cue-synth-withdrawal" : $"wf-drug-cue-withdrawal-body-{Math.Min(stage, 3)}", stage * 10 + 1);
                if (stage >= 3 && !host)
                    face.Offer("wf-drug-cue-withdrawal-face", stage * 10 + 1);
            }
        }

        // Pupils and skin need a close look at uncovered eyes; someone out cold isn't moving.
        var showFace = detailed && EyesVisible(uid);
        var showBody = !_mobState.IsIncapacitated(uid) && !HasComp<SleepingComponent>(uid);
        return (showFace ? face.Key : null, showBody ? body.Key : null);
    }

    private bool EyesVisible(EntityUid uid)
    {
        var ev = new SeeIdentityAttemptEvent();
        RaiseLocalEvent(uid, ev);
        return (ev.TotalCoverage & IdentityBlockerCoverage.EYES) == 0;
    }

    private static string PhaseName(WFDrugPhase phase)
    {
        return phase == WFDrugPhase.Overdose ? "overdose" : "heavy";
    }

    /// <summary>The strongest line offered so far.</summary>
    private struct Cue
    {
        public string? Key;
        private int _weight;

        public void Offer(string key, int weight)
        {
            if (Key != null && weight <= _weight)
                return;

            Key = key;
            _weight = weight;
        }
    }
}
