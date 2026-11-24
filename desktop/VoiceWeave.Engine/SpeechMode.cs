namespace VoiceWeave.Engine;

/// <summary>What the user chose to speak: one language, or English plus a regional language detected automatically.</summary>
public enum SpeechMode { English, Telugu, Hindi, Tamil, EnglishTelugu, EnglishHindi, EnglishTamil }

public static class SpeechModes
{
    public static readonly IReadOnlyList<SpeechMode> All = new[]
    {
        SpeechMode.English, SpeechMode.Telugu, SpeechMode.Hindi, SpeechMode.Tamil,
        SpeechMode.EnglishTelugu, SpeechMode.EnglishHindi, SpeechMode.EnglishTamil,
    };

    public static string Code(SpeechMode m) => m switch
    {
        SpeechMode.Telugu => "te",
        SpeechMode.Hindi => "hi",
        SpeechMode.Tamil => "ta",
        SpeechMode.EnglishTelugu => "en+te",
        SpeechMode.EnglishHindi => "en+hi",
        SpeechMode.EnglishTamil => "en+ta",
        _ => "en",
    };

    /// <summary>Also accepts the plain language codes saved by earlier versions.</summary>
    public static SpeechMode Parse(string? code) => code switch
    {
        "te" => SpeechMode.Telugu,
        "hi" => SpeechMode.Hindi,
        "ta" => SpeechMode.Tamil,
        "en+te" => SpeechMode.EnglishTelugu,
        "en+hi" => SpeechMode.EnglishHindi,
        "en+ta" => SpeechMode.EnglishTamil,
        _ => SpeechMode.English,
    };

    public static string DisplayName(SpeechMode m) => IsAuto(m)
        ? $"English + {Languages.DisplayName(Partner(m)!.Value)} (auto)"
        : Languages.DisplayName(Primary(m));

    public static bool IsAuto(SpeechMode m) => m is SpeechMode.EnglishTelugu or SpeechMode.EnglishHindi or SpeechMode.EnglishTamil;

    /// <summary>The regional language of an auto mode, or null for a single-language mode.</summary>
    public static Language? Partner(SpeechMode m) => m switch
    {
        SpeechMode.EnglishTelugu => Language.Telugu,
        SpeechMode.EnglishHindi => Language.Hindi,
        SpeechMode.EnglishTamil => Language.Tamil,
        _ => null,
    };

    /// <summary>The language a single-language mode recognises; English for the auto modes.</summary>
    public static Language Primary(SpeechMode m) => m switch
    {
        SpeechMode.Telugu => Language.Telugu,
        SpeechMode.Hindi => Language.Hindi,
        SpeechMode.Tamil => Language.Tamil,
        _ => Language.English,
    };

    /// <summary>The recognisers that must be loaded for this mode.</summary>
    public static IReadOnlyList<Language> Needs(SpeechMode m) =>
        Partner(m) is { } partner ? new[] { Language.English, partner } : new[] { Primary(m) };
}
