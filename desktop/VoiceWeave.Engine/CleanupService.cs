using VoiceWeave.Core;

namespace VoiceWeave.Engine;

public enum Tone { Formal, Casual, VeryCasual }

/// <summary>A text-cleanup model: raw dictated text in, tidy text out.</summary>
public interface ICleaner : IDisposable
{
    string Clean(string text, Tone tone);

    /// <param name="vocabulary">Names and terms the person uses, so the model can spell them their way. A cleaner that cannot use them ignores them.</param>
    string Clean(string text, Tone tone, IReadOnlyList<string>? vocabulary) => Clean(text, tone);
}

/// <summary>
/// Runs the cleanup model only when the text needs it, and refuses its output when it looks wrong:
/// empty, runaway, a protected name changed, or the model failed. The fallback is the input with filler noise stripped.
/// </summary>
public sealed class CleanupService(ICleaner cleaner)
{
    /// <summary>Most terms shown to the model, and the most characters they may take: every word costs time, and the model's window is small.</summary>
    public const int MaxVocabularyTerms = 20, MaxVocabularyChars = 240;

    /// <summary>When true the person's most recent terms go into the model's prompt (slower, but the model spells them their way).</summary>
    public Func<bool> UseVocabulary { get; set; } = () => false;

    /// <summary>The newest terms that fit: no commas or colons (they would break the list), no duplicates, within the limits.</summary>
    public static IReadOnlyList<string> PickVocabulary(IReadOnlyList<string> terms)
    {
        var picked = new List<string>();
        var chars = 0;
        for (var i = terms.Count - 1; i >= 0 && picked.Count < MaxVocabularyTerms; i--)
        {
            var term = terms[i].Trim();
            if (term.Length == 0 || term.IndexOfAny(new[] { ',', ':', '\n', '\r' }) >= 0) continue;
            if (picked.Contains(term, StringComparer.OrdinalIgnoreCase)) continue;
            if (chars + term.Length + 2 > MaxVocabularyChars) break;
            picked.Add(term);
            chars += term.Length + 2;
        }
        picked.Reverse(); // oldest first, as in the dictionary
        return picked;
    }

    public string Run(string text, Tone tone, IReadOnlyList<string> protectedTerms)
    {
        var input = CleanupGate.StripNoise(CleanupGate.CollapseRepeats(text));
        if (input.Length == 0) return input;
        var fallback = CleanupGate.Tidy(input, protectedTerms); // capital letter and full stop, with no model involved
        if (!CleanupGate.NeedsCleanup(input)) return fallback;

        var vocabulary = UseVocabulary() ? PickVocabulary(protectedTerms) : null;
        string output;
        try { output = cleaner.Clean(input, tone, vocabulary is { Count: > 0 } ? vocabulary : null).Trim(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or ObjectDisposedException or NotSupportedException)
        {
            return fallback;
        }

        if (output.Length == 0) return fallback;
        if (output.Length > input.Length * 2 + 20) return fallback; // model rambled or followed an instruction in the text
        if (vocabulary is { Count: > 0 } && LeaksPrompt(input, output)) return fallback; // it repeated the word list instead of the sentence
        if (!CleanupGate.ProtectedTermsIntact(input, output, protectedTerms)) return fallback;
        return output;
    }

    // The prompt's own labels must never come back in the answer unless the person actually said them.
    private static bool LeaksPrompt(string input, string output) =>
        (output.Contains("terms:", StringComparison.OrdinalIgnoreCase) && !input.Contains("terms:", StringComparison.OrdinalIgnoreCase)) ||
        (output.Contains("tone:", StringComparison.OrdinalIgnoreCase) && !input.Contains("tone:", StringComparison.OrdinalIgnoreCase));
}
