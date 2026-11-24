using System.Text;
using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

/// <summary>Personal vocabulary: "heard => preferred" rules, applied exactly then fuzzily.</summary>
public sealed class Dictionary
{
    private static readonly Regex WordRx = new(@"[\p{L}\p{N}'’]+", RegexOptions.Compiled);

    private readonly record struct Rule(string[] Heard, string Preferred);

    private readonly List<Rule> _rules;
    private readonly List<string> _fuzzyTargets; // single-word preferred forms
    private readonly List<string> _terms;

    private Dictionary(List<Rule> rules, List<string> terms)
    {
        _rules = rules.OrderByDescending(r => r.Heard.Length).ToList();
        _terms = terms;
        _fuzzyTargets = terms.Where(t => t.IndexOf(' ') < 0 && t.Length >= 4).Distinct().ToList();
    }

    public IReadOnlyList<string> Terms => _terms;

    public static Dictionary Parse(string text)
    {
        var rules = new List<Rule>();
        var terms = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var i = line.IndexOf("=>", StringComparison.Ordinal);
            if (i <= 0) continue;
            var heard = line[..i].Trim();
            var preferred = line[(i + 2)..].Trim();
            if (heard.Length == 0 || preferred.Length == 0) continue;
            var words = heard.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rules.Add(new Rule(words, preferred));
            if (!terms.Contains(preferred)) terms.Add(preferred);
        }
        return new Dictionary(rules, terms);
    }

    public string Apply(string text)
    {
        var tokens = WordRx.Matches(text);
        if (tokens.Count == 0 || _rules.Count == 0) return text;

        var sb = new StringBuilder();
        var pos = 0;
        var i = 0;
        while (i < tokens.Count)
        {
            var (consumed, replacement) = MatchPhrase(text, tokens, i);
            if (consumed == 0)
            {
                var tok = tokens[i];
                replacement = FuzzyReplace(tok.Value);
                consumed = 1;
                if (replacement == null) { i++; continue; }
            }
            var first = tokens[i];
            var last = tokens[i + consumed - 1];
            sb.Append(text, pos, first.Index - pos);
            sb.Append(replacement);
            pos = last.Index + last.Length;
            i += consumed;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    private (int consumed, string? replacement) MatchPhrase(string text, MatchCollection tokens, int start)
    {
        foreach (var rule in _rules)
        {
            var n = rule.Heard.Length;
            if (start + n > tokens.Count) continue;
            var ok = true;
            for (var k = 0; k < n && ok; k++)
            {
                if (!string.Equals(tokens[start + k].Value, rule.Heard[k], StringComparison.OrdinalIgnoreCase)) ok = false;
                if (ok && k > 0)
                {
                    var prev = tokens[start + k - 1];
                    var gap = text.Substring(prev.Index + prev.Length, tokens[start + k].Index - (prev.Index + prev.Length));
                    if (gap.Trim().Length != 0) ok = false; // punctuation between words breaks a phrase
                }
            }
            if (ok) return (n, rule.Preferred);
        }
        return (0, null);
    }

    private string? FuzzyReplace(string word)
    {
        if (word.Length < 4) return null;
        foreach (var target in _fuzzyTargets)
        {
            if (string.Equals(word, target, StringComparison.Ordinal)) return null;
            if (char.ToLowerInvariant(word[0]) != char.ToLowerInvariant(target[0])) continue;
            if (Math.Abs(word.Length - target.Length) > 1) continue;
            if (EditDistance.Damerau(word.ToLowerInvariant(), target.ToLowerInvariant()) <= 2)
                return target;
        }
        return null;
    }
}

internal static class EditDistance
{
    public static int Damerau(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    public static int Levenshtein<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) where T : IEquatable<T>
    {
        var prev = new int[b.Count + 1];
        var cur = new int[b.Count + 1];
        for (var j = 0; j <= b.Count; j++) prev[j] = j;
        for (var i = 1; i <= a.Count; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Count; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1].Equals(b[j - 1]) ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Count];
    }
}
