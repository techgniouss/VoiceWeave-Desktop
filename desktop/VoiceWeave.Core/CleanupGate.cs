using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

/// <summary>Decides whether dictated text is worth sending to the cleanup model, and guards its output.</summary>
public static class CleanupGate
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Filler = new(@"(?<![\p{L}\p{N}])(?:um+|uh+|er+m?|hmm+|mhm|mm-hmm|you know|i mean)(?![\p{L}\p{N}])", Opts);
    private static readonly Regex Correction = new(@"(?<![\p{L}\p{N}])(?:no wait|scratch that|sorry i mean|actually no)(?![\p{L}\p{N}])", Opts);
    private static readonly Regex NumberWord = new(@"(?<![\p{L}\p{N}])(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|million|percent)(?![\p{L}\p{N}])", Opts);
    private static readonly Regex Spoken = new(@"(?<![\p{L}\p{N}])dot(?![\p{L}\p{N}])|(?<![\p{L}\p{N}])slash(?![\p{L}\p{N}])", Opts);

    // Removes standalone noise tokens plus the punctuation right after them; never touches words that merely contain them.
    private static readonly Regex Noise = new(@"(?<![\p{L}\p{N}])(?:mm-hmm|mhm|hmm+|hm+|um+|uh+|erm|er)(?![\p{L}\p{N}])[.,;!?]?\s*", Opts);

    public static bool NeedsCleanup(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (Filler.IsMatch(t) || Correction.IsMatch(t) || NumberWord.IsMatch(t) || Spoken.IsMatch(t)) return true;
        if (char.IsLetter(t[0]) && !char.IsUpper(t[0])) return true;
        return !EndsSentence(t);
    }

    private static bool EndsSentence(string t) => ".?!".Contains(t[^1]);

    public static string StripNoise(string text)
    {
        var s = Noise.Replace(text, "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s;
    }

    public static bool ProtectedTermsIntact(string before, string after, IReadOnlyList<string> terms)
    {
        foreach (var term in terms)
        {
            if (before.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !after.Contains(term, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
