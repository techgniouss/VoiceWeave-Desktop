using System.IO.Compression;
using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

/// <summary>
/// Tells real English apart from what an English recogniser makes of another language ("Aim Chesto naura" for Telugu
/// "em chestunnav ra"): the share of words that are in a large English word list. The list is the public-domain
/// dwyl/english-words (words_alpha) file, stored compressed in the app.
/// </summary>
public static class EnglishWords
{
    private static readonly Lazy<HashSet<string>> Lexicon = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Regex Token = new(@"[A-Za-z]+(?:'[A-Za-z]+)*", RegexOptions.Compiled);

    private static HashSet<string> Load()
    {
        using var resource = typeof(EnglishWords).Assembly.GetManifestResourceStream("VoiceWeave.Core.Resources.english-words.txt.gz")
            ?? throw new InvalidOperationException("The English word list is missing from the app.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var set = new HashSet<string>(400_000, StringComparer.Ordinal);
        for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
            if (line.Length > 0) set.Add(line);
        return set;
    }

    public static bool IsWord(string word) => IsKnown(word.ToLowerInvariant(), null);

    /// <summary>
    /// The fraction (0 to 1) of the words in <paramref name="text"/> that are English. Single letters other than "a" and "I",
    /// digits and punctuation are ignored. <paramref name="knownTerms"/> (for example the user's dictionary) count as words.
    /// </summary>
    public static double Plausibility(string text, IReadOnlySet<string>? knownTerms = null)
    {
        var total = 0;
        var known = 0;
        foreach (Match m in Token.Matches(text))
        {
            var word = m.Value.ToLowerInvariant();
            if (word.Length == 1 && word != "a" && word != "i") continue;
            total++;
            if (IsKnown(word, knownTerms)) known++;
        }
        return total == 0 ? 0.0 : (double)known / total;
    }

    private static bool IsKnown(string word, IReadOnlySet<string>? terms)
    {
        if (terms != null && terms.Contains(word)) return true;
        var lexicon = Lexicon.Value;
        if (lexicon.Contains(word)) return true;
        var apostrophe = word.IndexOf('\'');
        if (apostrophe < 0) return false;
        // possessives and contractions: john's -> john, don't -> don + t is not a word, so also try dropping "n't"/"'ll" forms
        var stem = word[..apostrophe];
        if (word.EndsWith("n't", StringComparison.Ordinal)) return lexicon.Contains(word[..^3]) || lexicon.Contains(word.Replace("'", ""));
        return lexicon.Contains(stem) || (terms != null && terms.Contains(stem));
    }
}
