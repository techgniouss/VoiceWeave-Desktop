using VoiceWeave.Core;

namespace VoiceWeave.Engine;

public enum Tone { Formal, Casual, VeryCasual }

/// <summary>A text-cleanup model: raw dictated text in, tidy text out.</summary>
public interface ICleaner : IDisposable
{
    string Clean(string text, Tone tone);
}

/// <summary>
/// Runs the cleanup model only when the text needs it, and refuses its output when it looks wrong:
/// empty, runaway, a protected name changed, or the model failed. The fallback is the input with filler noise stripped.
/// </summary>
public sealed class CleanupService(ICleaner cleaner)
{
    public string Run(string text, Tone tone, IReadOnlyList<string> protectedTerms)
    {
        var input = CleanupGate.StripNoise(CleanupGate.CollapseRepeats(text));
        if (input.Length == 0) return input;
        var fallback = CleanupGate.Tidy(input, protectedTerms); // capital letter and full stop, with no model involved
        if (!CleanupGate.NeedsCleanup(input)) return fallback;

        string output;
        try { output = cleaner.Clean(input, tone).Trim(); }
        catch (Exception e) when (e is InvalidOperationException or IOException or ObjectDisposedException or NotSupportedException)
        {
            return fallback;
        }

        if (output.Length == 0) return fallback;
        if (output.Length > input.Length * 2 + 20) return fallback; // model rambled or followed an instruction in the text
        if (!CleanupGate.ProtectedTermsIntact(input, output, protectedTerms)) return fallback;
        return output;
    }
}
