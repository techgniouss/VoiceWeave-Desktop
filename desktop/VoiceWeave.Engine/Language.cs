namespace VoiceWeave.Engine;

public enum Language { English, Telugu, Hindi, Tamil }

public static class Languages
{
    public static readonly IReadOnlyList<Language> All = new[] { Language.English, Language.Telugu, Language.Hindi, Language.Tamil };

    public static string Code(Language l) => l switch
    {
        Language.Telugu => "te",
        Language.Hindi => "hi",
        Language.Tamil => "ta",
        _ => "en",
    };

    public static string DisplayName(Language l) => l.ToString();

    public static Language Parse(string? code) => code switch
    {
        "te" => Language.Telugu,
        "hi" => Language.Hindi,
        "ta" => Language.Tamil,
        _ => Language.English,
    };

    /// <summary>The cleanup model is English-only; regional text is kept as recognised (plus the dictionary).</summary>
    public static bool UsesCleanup(Language l) => l == Language.English;
}
