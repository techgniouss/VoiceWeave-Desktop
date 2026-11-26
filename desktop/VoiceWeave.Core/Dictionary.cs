using System.Text;
using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

/// <summary>A dictionary rule that changed some text: its line in the dictionary file and the spelling it produced.</summary>
/// <param name="Approximate">True when the rule matched by a near-miss or a sound-alike guess rather than by its own words.</param>
public sealed record AppliedRule(string Line, string Preferred, bool Approximate = false);

/// <summary>Personal vocabulary: "heard => preferred" rules, applied exactly then fuzzily.</summary>
public sealed class Dictionary
{
    private static readonly Regex WordRx = new(@"[\p{L}\p{M}\p{N}'’]+", RegexOptions.Compiled); // \p{M}: vowel signs and viramas inside Indic words

    private readonly record struct Rule(string[] Heard, string Preferred, string Line);

    private readonly List<Rule> _rules;
    private readonly List<string> _fuzzyTargets; // single-word preferred forms
    private readonly List<string> _terms;
    private readonly System.Collections.Generic.Dictionary<string, string> _lineOfPreferred = new(StringComparer.Ordinal);

    private const int MaxSoundWords = 3;       // "sri kanth" or "argo c d": up to three spoken words stand for one term
    private const int MinSoundLetters = 5;     // shorter terms sound like too many other things

    private readonly record struct SoundTarget(string Preferred, string Squashed, string Key);
    private readonly List<SoundTarget> _soundTargets;

    private Dictionary(List<Rule> rules, List<string> terms, bool soundAlike)
    {
        _rules = rules.OrderByDescending(r => r.Heard.Length).ToList();
        _terms = terms;
        // Near-miss matching is for English-looking words: two letters off is a typo there, but in Telugu or Hindi it is a different word.
        _fuzzyTargets = terms.Where(t => t.IndexOf(' ') < 0 && t.Length >= 4 && IsPlain(t)).Distinct().ToList();
        foreach (var r in rules) _lineOfPreferred.TryAdd(r.Preferred, r.Line);
        _soundTargets = !soundAlike ? new List<SoundTarget>() : terms
            .Select(t => (Term: t, Squashed: SoundAlike.Squash(t)))
            .Where(t => t.Squashed.Length >= MinSoundLetters)
            .Select(t => new SoundTarget(t.Term, t.Squashed, SoundAlike.Key(t.Squashed)))
            .ToList();
    }

    public IReadOnlyList<string> Terms => _terms;

    /// <param name="soundAlike">Also match words that sound like a term ("Sreekant" for Srikanth). A guess, so it can be turned off.</param>
    public static Dictionary Parse(string text, bool soundAlike = true)
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
            rules.Add(new Rule(words, preferred, raw.TrimEnd('\r')));
            if (!terms.Contains(preferred)) terms.Add(preferred);
        }
        return new Dictionary(rules, terms, soundAlike);
    }

    /// <param name="fired">When given, receives each rule that changed the text, so a later correction of its output can be traced back to it.</param>
    public string Apply(string text, ICollection<AppliedRule>? fired = null) => Apply(text, fired, allowApproximate: true);

    /// <summary>Only the rules' own words, no near-miss or sound-alike guesses (used to tell whether a phrase already has a rule).</summary>
    public string ApplyExact(string text) => Apply(text, null, allowApproximate: false);

    private string Apply(string text, ICollection<AppliedRule>? fired, bool allowApproximate)
    {
        var tokens = WordRx.Matches(text);
        if (tokens.Count == 0 || _rules.Count == 0) return text;

        var sb = new StringBuilder();
        var pos = 0;
        var i = 0;
        while (i < tokens.Count)
        {
            var (consumed, replacement, line) = MatchPhrase(text, tokens, i);
            var approximate = false;
            if (consumed == 0)
            {
                if (!allowApproximate) { i++; continue; }
                approximate = true;
                // Several words that sound like a learned term ("sri kant" for Srikanth), then one word a letter or two off it.
                (consumed, replacement) = SoundAlikeMatch(text, tokens, i, minWords: 2);
                if (consumed == 0)
                {
                    replacement = FuzzyReplace(tokens[i].Value);
                    consumed = 1;
                    if (replacement == null) (consumed, replacement) = SoundAlikeMatch(text, tokens, i, minWords: 1);
                }
                if (consumed == 0 || replacement == null) { consumed = 0; i++; continue; }
                line = _lineOfPreferred.GetValueOrDefault(replacement);
                if (consumed > 0 && IsAlready(text, tokens, i, consumed, replacement)) line = null; // already right: not this rule's doing
            }
            if (fired != null && line != null)
            {
                var earlier = fired.FirstOrDefault(f => f.Line == line);
                if (earlier == null) fired.Add(new AppliedRule(line, replacement!, approximate));
                else if (earlier.Approximate && !approximate) { fired.Remove(earlier); fired.Add(new AppliedRule(line, replacement!)); } // an exact hit counts most
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

    private (int consumed, string? replacement, string? line) MatchPhrase(string text, MatchCollection tokens, int start)
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
            if (ok) return (n, rule.Preferred, rule.Line);
        }
        return (0, null, null);
    }

    private static bool IsAlready(string text, MatchCollection tokens, int start, int count, string target)
    {
        var first = tokens[start];
        var last = tokens[start + count - 1];
        return string.Equals(text.Substring(first.Index, last.Index + last.Length - first.Index), target, StringComparison.Ordinal);
    }

    /// <summary>
    /// The longest run of words (at most three, with nothing but spaces between them) that sounds like a learned term: it spells
    /// the same once spaces are ignored ("argo c d"), or has the same first letter, a similar length and the same sound key
    /// ("Sreekant"). Never rewrites something that is a real English word.
    /// </summary>
    private (int consumed, string? replacement) SoundAlikeMatch(string text, MatchCollection tokens, int start, int minWords)
    {
        if (_soundTargets.Count == 0) return (0, null);
        for (var n = Math.Min(MaxSoundWords, tokens.Count - start); n >= minWords; n--)
        {
            var ok = true;
            for (var k = 1; k < n && ok; k++)
            {
                var prev = tokens[start + k - 1];
                ok = text.Substring(prev.Index + prev.Length, tokens[start + k].Index - (prev.Index + prev.Length)).Trim().Length == 0;
            }
            if (!ok) continue;

            var first = tokens[start];
            var last = tokens[start + n - 1];
            var raw = text.Substring(first.Index, last.Index + last.Length - first.Index);
            var squashed = SoundAlike.Squash(raw);
            if (squashed.Length < MinSoundLetters - 1) continue;
            var key = SoundAlike.Key(squashed);

            foreach (var target in _soundTargets)
            {
                var same = squashed == target.Squashed;
                if (!same)
                {
                    if (key.Length == 0 || key != target.Key || squashed[0] != target.Squashed[0]) continue;
                    var ratio = (double)squashed.Length / target.Squashed.Length;
                    if (ratio is < 0.75 or > 1.34) continue;
                }
                if (string.Equals(raw, target.Preferred, StringComparison.Ordinal)) return (n, target.Preferred); // already written that way
                // A real word is what was meant, unless the same letters were only spread over several words ("mark s" is a spelling
                // variant of the term, not a different word). The word list also holds many names, so it cannot veto those.
                if (!(same && n > 1) && EnglishWords.IsWord(squashed)) break;
                return (n, target.Preferred);
            }
        }
        return (0, null);
    }

    private static bool IsPlain(string word) => word.All(c => c < 128);

    private string? FuzzyReplace(string word)
    {
        if (word.Length < 4 || !IsPlain(word)) return null;
        foreach (var target in _fuzzyTargets)
        {
            if (string.Equals(word, target, StringComparison.Ordinal)) return null;
            if (char.ToLowerInvariant(word[0]) != char.ToLowerInvariant(target[0])) continue;
            if (Math.Abs(word.Length - target.Length) > 1) continue;
            if (EditDistance.Damerau(word.ToLowerInvariant(), target.ToLowerInvariant()) <= 2)
            {
                // A real English word is what was meant ("Masks" is not "Marks"): only a near-miss that is no word gets rewritten.
                if (EnglishWords.IsWord(word)) return null;
                return target;
            }
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
