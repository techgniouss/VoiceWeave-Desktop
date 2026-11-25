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

    // A lone "one" or "two" is usually just a word ("one of the", "two of us"); numbers worth converting come in pairs, with a scale or with a unit.
    private static readonly Regex NumberUnit = new(@"(?<![\p{L}\p{N}])(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)\s+(?:a\.?m\.?|p\.?m\.?|o'?clock|dollars?|bucks|rupees|euros?|pounds?|cents?|minutes?|mins?|hours?|hrs?|seconds?|days?|weeks?|months?|years?|k|km|kg|gb|mb|times|x)(?![\p{L}\p{N}])", Opts);
    private static readonly Regex Scale = new(@"(?<![\p{L}\p{N}])(?:hundred|thousand|million|billion|percent)(?![\p{L}\p{N}])", Opts);
    private static readonly Regex QuestionLike = new(
        @"^[""'(]*(?:(?:what|when|where|why|how|who|whom|whose|which)\b|(?:can|could|would|will|should|is|are|was|were|have|has|had|do|does|did|am|may|might|shall)\s+(?:you|i|we|they|he|she|it|there|this|that|anyone|everyone|someone|somebody|anybody)\b)", Opts);

    /// <summary>
    /// True when the model can add something code cannot: removing fillers, resolving "no wait" corrections, writing numbers and
    /// "dot"/"slash" as symbols, or punctuating what looks like a question. A missing capital letter or full stop is fixed by
    /// <see cref="Tidy"/>, which costs nothing, so those alone no longer send text to the model.
    /// </summary>
    public static bool NeedsCleanup(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return false;
        if (Filler.IsMatch(t) || Correction.IsMatch(t) || Spoken.IsMatch(t)) return true;
        if (Scale.IsMatch(t) || NumberUnit.IsMatch(t) || NumberWord.Matches(t).Count >= 2) return true;
        return !EndsSentence(t) && QuestionLike.IsMatch(t);
    }

    private static bool EndsSentence(string t) => ".?!…".Contains(t[^1]);

    /// <summary>Capital first letter and a closing full stop, for text the model does not need to see. Terms the user protects keep their spelling.</summary>
    public static string Tidy(string text, IReadOnlyList<string>? protectedTerms = null)
    {
        var t = text.Trim();
        if (t.Length == 0) return t;
        var end = 0;
        while (end < t.Length && (char.IsLetterOrDigit(t[end]) || t[end] is '\'' or '’' or '-')) end++;
        var firstWord = t[..end];
        var keepsSpelling = firstWord.Skip(1).Any(char.IsUpper) || firstWord.Any(char.IsDigit)
            || (protectedTerms?.Any(term => string.Equals(term, firstWord, StringComparison.OrdinalIgnoreCase)) ?? false);
        if (char.IsLetter(t[0]) && char.IsLower(t[0]) && !keepsSpelling) t = char.ToUpperInvariant(t[0]) + t[1..];
        if (char.IsLetterOrDigit(t[^1]) || t[^1] is '"' or ')' or '\'') t += ".";
        return t;
    }

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
