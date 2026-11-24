using System.Text;

namespace VoiceWeave.Core;

/// <summary>Word/character error rates and term accuracy; normalisation matches score-dictation.py.</summary>
public static class Scoring
{
    public static string[] NormalizeWords(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (ch == '-') sb.Append(' ');
            else if (char.IsLetterOrDigit(ch) || ch == '\'' || ch == ' ') sb.Append(ch);
            else sb.Append(' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static (int errors, int refWords) Wer(string reference, string hypothesis)
    {
        var r = NormalizeWords(reference);
        var h = NormalizeWords(hypothesis);
        return (EditDistance.Levenshtein<string>(r, h), r.Length);
    }

    public static (int errors, int refChars) Cer(string reference, string hypothesis)
    {
        var r = string.Join(' ', NormalizeWords(reference)).ToCharArray();
        var h = string.Join(' ', NormalizeWords(hypothesis)).ToCharArray();
        return (EditDistance.Levenshtein<char>(r, h), r.Length);
    }

    /// <summary>Counts how many terms appear exactly (case-sensitive) in the hypothesis.</summary>
    public static (int found, int total) TermAccuracy(IReadOnlyList<string> terms, string hypothesis)
    {
        var found = terms.Count(t => hypothesis.Contains(t, StringComparison.Ordinal));
        return (found, terms.Count);
    }
}
