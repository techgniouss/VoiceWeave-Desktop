using VoiceWeave.Core;
namespace VoiceWeave.Engine;

/// <summary>One recognised stretch of speech and the language it was in.</summary>
public sealed record Segment(string Text, Language Language);

/// <summary>Identifies the spoken language of a short recording; returns a Whisper-style code such as "en" or "te".</summary>
public interface ILanguageIdentifier
{
    string Identify(float[] pcm16k);
}

/// <summary>Turns a Whisper language-ID result into one of the two languages of an auto mode.</summary>
public static class LanguageChooser
{
    private const double TooShortToJudgeSeconds = 1.0;

    // Language ID often confuses one Indian language with another, so any of these means "the regional language".
    private static readonly HashSet<string> Indian = new()
    {
        "te", "hi", "ta", "ml", "kn", "mr", "bn", "gu", "pa", "ur", "ne", "sa", "as", "or", "sd",
    };

    public static Language Choose(string code, Language partner, double seconds, Language? previous)
    {
        if (seconds < TooShortToJudgeSeconds && previous is { } p) return p; // a word or two is too little to identify
        if (code == "en") return Language.English;
        if (Indian.Contains(code)) return partner;
        return previous ?? Language.English; // some unrelated language was guessed: stay with what we were doing
    }
}

/// <summary>
/// Recognises one stretch of speech with the engine of the chosen mode. In the auto modes the English recogniser always goes
/// first: if its words are clearly English the sentence is English, if they are clearly not ("Aim Chesto naura") it is the
/// regional language, and only the unclear cases need language identification.
/// </summary>
public sealed class SpeechRouter(Func<Language, IAsr> engines, Func<ILanguageIdentifier?> languageId, SpeechMode initial)
{
    /// <summary>At or above this share of English words the sentence is English without asking anyone else.</summary>
    public const double ClearlyEnglish = 0.85;
    /// <summary>At or below this share it is not English.</summary>
    public const double ClearlyNotEnglish = 0.40;

    private volatile int _mode = (int)initial;

    /// <summary>Optional diagnostics: how long each step took and which way it was decided.</summary>
    public Action<string>? Trace { get; set; }

    /// <summary>Words that count as English even though a dictionary would not know them (the user's names and terms).</summary>
    public Func<IReadOnlySet<string>>? KnownTerms { get; set; }

    public SpeechMode Mode
    {
        get => (SpeechMode)_mode;
        set => _mode = (int)value;
    }

    /// <param name="previous">The language of the stretch before this one, if any (used when this one is too short to judge).</param>
    /// <returns>The recognised text, or null when nothing was recognised.</returns>
    public Segment? RecognizeSegment(float[] pcm16k, Language? previous, bool forceRegional = false)
    {
        var mode = Mode;
        var seconds = pcm16k.Length / 16000.0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        if (SpeechModes.Partner(mode) is not { } partner)
        {
            var only = SpeechModes.Primary(mode);
            var single = engines(only).Transcribe(pcm16k).Trim();
            Trace?.Invoke($"{seconds:F1}s of speech: {only} recognition {clock.Elapsed.TotalSeconds:F2}s");
            return single.Length == 0 ? null : new Segment(single, only);
        }

        if (forceRegional)
        {
            // The user said which language this is (Shift held): no guessing.
            var forced = engines(partner).Transcribe(pcm16k).Trim();
            Trace?.Invoke($"{seconds:F1}s of speech: forced {partner}, recognition {clock.Elapsed.TotalSeconds:F2}s");
            return forced.Length == 0 ? null : new Segment(forced, partner);
        }

        var english = engines(Language.English).Transcribe(pcm16k).Trim();
        var englishTime = clock.Elapsed;
        var score = EnglishWords.Plausibility(english, KnownTerms?.Invoke());

        string? code = null;
        Language language;
        string how;
        if (score >= ClearlyEnglish) { language = Language.English; how = "clearly English"; }
        else if (score <= ClearlyNotEnglish) { language = partner; how = "not English"; }
        else
        {
            var lid = languageId();
            if (lid == null) { language = Language.English; how = "unclear, no language id yet"; }
            else
            {
                code = lid.Identify(pcm16k);
                language = LanguageChooser.Choose(code, partner, seconds, previous);
                how = $"unclear, language id '{code}'";
            }
        }

        var text = english;
        if (language != Language.English)
        {
            clock.Restart();
            text = engines(language).Transcribe(pcm16k).Trim();
        }
        Trace?.Invoke($"{seconds:F1}s of speech: English {englishTime.TotalSeconds:F2}s, English-word share {score:P0} ({how}) -> {language}, total {(englishTime + (language == Language.English ? TimeSpan.Zero : clock.Elapsed)).TotalSeconds:F2}s");
        return text.Length == 0 ? null : new Segment(text, language);
    }
}
