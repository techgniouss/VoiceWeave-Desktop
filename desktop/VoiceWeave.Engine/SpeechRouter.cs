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

/// <summary>Recognises one stretch of speech with the engine of the chosen mode, picking per stretch in auto modes.</summary>
public sealed class SpeechRouter(Func<Language, IAsr> engines, Func<ILanguageIdentifier?> languageId, SpeechMode initial)
{
    private volatile int _mode = (int)initial;

    /// <summary>Optional diagnostics: how long language detection and recognition took for each stretch.</summary>
    public Action<string>? Trace { get; set; }

    public SpeechMode Mode
    {
        get => (SpeechMode)_mode;
        set => _mode = (int)value;
    }

    /// <param name="previous">The language of the stretch before this one, if any (used when this one is too short to judge).</param>
    /// <returns>The recognised text, or null when nothing was recognised.</returns>
    public Segment? RecognizeSegment(float[] pcm16k, Language? previous)
    {
        var mode = Mode;
        var language = SpeechModes.Primary(mode);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string? code = null;
        if (SpeechModes.Partner(mode) is { } partner)
        {
            var lid = languageId();
            if (lid == null) language = Language.English; // the language-ID model is still loading: assume English rather than wait
            else
            {
                code = lid.Identify(pcm16k);
                language = LanguageChooser.Choose(code, partner, pcm16k.Length / 16000.0, previous);
            }
        }
        var idTime = clock.Elapsed;
        clock.Restart();
        var text = engines(language).Transcribe(pcm16k).Trim();
        Trace?.Invoke($"{pcm16k.Length / 16000.0:F1}s of speech: language id {idTime.TotalSeconds:F2}s ({code ?? "-"} -> {language}), recognition {clock.Elapsed.TotalSeconds:F2}s");
        return text.Length == 0 ? null : new Segment(text, language);
    }
}
