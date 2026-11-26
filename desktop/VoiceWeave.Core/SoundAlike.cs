using System.Text;

namespace VoiceWeave.Core;

/// <summary>
/// A rough "how does it sound" key for English-looking words, so "Srikanth", "Sreekant" and "sri kant" can be recognised as the
/// same name. It keeps the consonants, folds letters that sound alike, and drops vowels, so it is a guess and is only ever used
/// together with other checks (same first letter, similar length, not a real English word).
/// </summary>
public static class SoundAlike
{
    /// <summary>
    /// Lower-case letters and digits of <paramref name="text"/>, with spaces and punctuation removed. Empty when the text has
    /// anything outside plain A-Z and 0-9 (Telugu, Hindi, accents): dropping combining marks there would make different words
    /// look the same, so such words only ever match by an exact rule.
    /// </summary>
    public static string Squash(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else if (c is >= 'A' and <= 'Z') sb.Append(char.ToLowerInvariant(c));
            else if (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
                     or System.Globalization.UnicodeCategory.SpacingCombiningMark) return "";
        }
        return sb.ToString();
    }

    /// <summary>The sound key of a squashed word. Words with no letters of the Latin alphabet give an empty key.</summary>
    public static string Key(string squashed)
    {
        if (squashed.Length == 0 || squashed.Any(c => c > 'z')) return ""; // other scripts have no key; the exact rules still work
        var s = squashed
            .Replace("x", "ks").Replace("ph", "f").Replace("ck", "k").Replace("sch", "sk").Replace("sh", "x").Replace("ch", "x")
            .Replace("th", "t").Replace("gh", "").Replace("wh", "w").Replace("qu", "kw").Replace("q", "k")
            .Replace("z", "s").Replace("v", "f").Replace("dg", "j");
        if (s.StartsWith("kn", StringComparison.Ordinal) || s.StartsWith("wr", StringComparison.Ordinal)) s = s[1..];

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == 'c') c = i + 1 < s.Length && s[i + 1] is 'e' or 'i' or 'y' ? 's' : 'k'; // soft or hard c
            if (c == 'h' && i > 0) continue;                                                  // "Srikanth" and "Srikant"
            if (IsVowel(c)) { if (i == 0) sb.Append('a'); continue; }                         // only a leading vowel counts
            if (sb.Length > 0 && sb[^1] == c) continue;                                       // doubled letters sound single
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
