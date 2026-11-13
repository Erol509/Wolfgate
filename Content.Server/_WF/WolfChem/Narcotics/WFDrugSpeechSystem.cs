using System.Text;
using System.Text.RegularExpressions;
using Content.Server._WF.Voice;
using Content.Server.Speech;
using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Shared.Random;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// How a high sounds to everyone. In text, a heavy stimulant high rushes speech, doubling words and turning full stops
/// into exclamations, and a heavy opioid high trails off into pauses. In the character's voice, each family shifts
/// syllable speed, pitch, intonation, waver and wobble by phase. An overdose pushes both further.
/// </summary>
public sealed class WFDrugSpeechSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    private static readonly Regex Token = new(@"\S+");
    private static readonly Regex FullStop = new(@"(?<=\p{L})\.(?=\s|$)");
    private static readonly Regex Ending = new(@"[.!?,;:]+$");

    /// <summary>
    /// How one phase of a family changes a voice: syllable time, pitch and intonation as multipliers, pitch variance
    /// added, vibrato depth and tremolo as floors. Families stack.
    /// </summary>
    private readonly record struct VoiceShift(
        float Speed = 1f,
        float Pitch = 1f,
        float Intonation = 1f,
        float Variance = 0f,
        float Vibrato = 0f,
        float Tremolo = 0f);

    private static readonly Dictionary<(WFDrugFamily, WFDrugPhase), VoiceShift> Voices = new()
    {
        [(WFDrugFamily.Stimulant, WFDrugPhase.Light)] = new(Speed: 0.85f),
        [(WFDrugFamily.Stimulant, WFDrugPhase.Heavy)] = new(Speed: 0.7f, Pitch: 1.04f),
        [(WFDrugFamily.Stimulant, WFDrugPhase.Overdose)] = new(Speed: 0.55f, Pitch: 1.08f, Variance: 0.1f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Light)] = new(Speed: 1.15f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Heavy)] = new(Speed: 1.4f, Pitch: 0.94f, Intonation: 0.5f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Overdose)] = new(Speed: 1.7f, Pitch: 0.9f, Intonation: 0f),
        [(WFDrugFamily.Cannabinoid, WFDrugPhase.Heavy)] = new(Speed: 1.2f, Intonation: 0.7f),
        [(WFDrugFamily.Cannabinoid, WFDrugPhase.Overdose)] = new(Speed: 1.35f, Intonation: 0.5f),
        [(WFDrugFamily.Psychedelic, WFDrugPhase.Heavy)] = new(Variance: 0.15f, Vibrato: 0.12f),
        [(WFDrugFamily.Psychedelic, WFDrugPhase.Overdose)] = new(Variance: 0.3f, Vibrato: 0.25f),
        [(WFDrugFamily.Empathogen, WFDrugPhase.Heavy)] = new(Pitch: 1.03f, Intonation: 1.6f),
        [(WFDrugFamily.Empathogen, WFDrugPhase.Overdose)] = new(Pitch: 1.05f, Intonation: 2f, Tremolo: 0.3f),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, AccentGetEvent>(OnAccent);
        SubscribeLocalEvent<WFIntoxicationComponent, SpeechVoiceModifyEvent>(OnVoice);
    }

    private void OnVoice(Entity<WFIntoxicationComponent> ent, ref SpeechVoiceModifyEvent args)
    {
        foreach (var family in Enum.GetValues<WFDrugFamily>())
        {
            if (!Voices.TryGetValue((family, _intoxication.FamilyPhase(ent.Comp, family)), out var shift))
                continue;

            var voice = args.Voice;
            voice.Speed *= shift.Speed;
            voice.Pitch *= shift.Pitch;
            voice.Intonation *= shift.Intonation;
            voice.PitchVariance += shift.Variance;
            voice.VibratoDepth = Math.Max(voice.VibratoDepth, shift.Vibrato);
            voice.Tremolo = Math.Max(voice.Tremolo, shift.Tremolo);
            args.Changed = true;
        }
    }

    private void OnAccent(EntityUid uid, WFIntoxicationComponent comp, AccentGetEvent args)
    {
        var stimulant = _intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant);
        if (stimulant >= WFDrugPhase.Heavy)
            args.Message = Rush(args.Message, stimulant == WFDrugPhase.Overdose);

        var opioid = _intoxication.FamilyPhase(comp, WFDrugFamily.Opioid);
        if (opioid >= WFDrugPhase.Heavy)
            args.Message = Trail(args.Message, opioid == WFDrugPhase.Overdose);
    }

    /// <summary>Doubles some words, shouts some in an overdose, and ends sentences with exclamations.</summary>
    public string Rush(string message, bool overdose)
    {
        var repeat = overdose ? 0.3f : 0.15f;
        var shout = overdose ? 0.3f : 0f;
        var rushed = Token.Replace(message, match =>
        {
            var word = match.Value;
            if (_random.Prob(shout))
                word = word.ToUpperInvariant();

            var core = Ending.Replace(word, string.Empty);
            return core.Length > 0 && _random.Prob(repeat) ? $"{core}, {word}" : word;
        });

        return FullStop.Replace(rushed, "!");
    }

    /// <summary>Puts pauses after some words and lets the end trail off.</summary>
    public string Trail(string message, bool overdose)
    {
        var pause = overdose ? 0.25f : 0.12f;
        var tokens = Token.Matches(message);
        var result = new StringBuilder();
        var from = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result.Append(message, from, token.Index - from);
            from = token.Index + token.Length;

            var last = i == tokens.Count - 1;
            if (last)
            {
                result.Append(Ending.Replace(token.Value, string.Empty)).Append("...");
                continue;
            }

            result.Append(token.Value);
            if (!Ending.IsMatch(token.Value) && _random.Prob(pause))
                result.Append("...");
        }

        result.Append(message, from, message.Length - from);
        return result.ToString();
    }
}
