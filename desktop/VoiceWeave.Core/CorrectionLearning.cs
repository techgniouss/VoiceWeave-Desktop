using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

/// <summary>
/// One fix the user made to a dictation. <see cref="Before"/> and <see cref="After"/> are the changed words with one neighbour on
/// each side. A general fix is safe to apply everywhere; otherwise it only makes sense in that phrase.
/// </summary>
public sealed record LearnedEdit(string Before, string After, string OldWord, string NewWord, bool General);

/// <summary>Finds word-level fixes the user made to a dictation so they can become dictionary rules (ported from the Android app).</summary>
public static class CorrectionLearning
{
    // Letters, marks and digits, joined by an apostrophe, hyphen or dot inside a word: don't, e-mail, node.js, k8s.
    private static readonly Regex Word = new(@"[\p{L}\p{M}\p{N}]+(?:['’.\-][\p{L}\p{M}\p{N}]+)*", RegexOptions.Compiled);
    private const int MaxTokensPerSide = 3;

    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "to", "of", "in", "on", "at", "by", "for", "and", "or", "but", "so", "if", "is", "are", "was", "were",
        "be", "been", "it", "its", "this", "that", "these", "those", "i", "we", "you", "he", "she", "they", "me", "us", "him",
        "her", "them", "my", "our", "your", "his", "their", "with", "from", "as", "not", "no", "yes", "do", "does", "did", "have",
        "has", "had", "will", "would", "can", "could", "should", "there", "here", "then", "than", "what", "when", "where", "who",
        "how", "why", "which", "one", "two", "all", "any", "some",
    };

    private readonly record struct Token(string Text, int Start, int End);

    /// <summary>Every single-spot replacement between what was dictated and what the field holds now.</summary>
    public static IReadOnlyList<LearnedEdit> SuggestAll(string original, string updated)
    {
        var result = new List<LearnedEdit>();
        var prior = Tokens(original);
        var later = Tokens(updated);
        if (prior.Count == 0 || later.Count == 0 || (long)prior.Count * later.Count > 250_000L) return result;

        // Longest common subsequence of identical tokens; everything else is a change.
        int rows = prior.Count, columns = later.Count;
        var length = new int[rows + 1, columns + 1];
        for (var i = rows - 1; i >= 0; i--)
            for (var j = columns - 1; j >= 0; j--)
                length[i, j] = prior[i].Text == later[j].Text ? length[i + 1, j + 1] + 1 : Math.Max(length[i + 1, j], length[i, j + 1]);

        int a = 0, b = 0;
        while (a < rows || b < columns)
        {
            if (a < rows && b < columns && prior[a].Text == later[b].Text) { a++; b++; continue; }
            int oldStart = a, newStart = b;
            while ((a < rows || b < columns) && !(a < rows && b < columns && prior[a].Text == later[b].Text))
            {
                if (b >= columns || (a < rows && length[a + 1, b] >= length[a, b + 1])) a++;
                else b++;
            }
            var edit = Hunk(original, updated, prior, later, oldStart, a, newStart, b);
            if (edit != null) result.Add(edit);
        }
        return result;
    }

    private static LearnedEdit? Hunk(string original, string updated, List<Token> prior, List<Token> later,
        int oldStart, int oldEnd, int newStart, int newEnd)
    {
        int oldCount = oldEnd - oldStart, newCount = newEnd - newStart;
        if (oldCount < 1 || newCount < 1 || oldCount > MaxTokensPerSide || newCount > MaxTokensPerSide) return null;
        var oldWords = original[prior[oldStart].Start..prior[oldEnd - 1].End];
        var newWords = updated[later[newStart].Start..later[newEnd - 1].End];
        if (oldWords == newWords || oldWords.Length > 48 || newWords.Length > 48 || Unsafe(oldWords) || Unsafe(newWords)) return null;
        var firstOld = Math.Max(0, oldStart - 1);
        var lastOld = Math.Min(prior.Count - 1, oldEnd);
        var firstNew = Math.Max(0, newStart - 1);
        var lastNew = Math.Min(later.Count - 1, newEnd);
        var before = original[prior[firstOld].Start..prior[lastOld].End];
        var after = updated[later[firstNew].Start..later[lastNew].End];
        if (before.Length > 90 || after.Length > 90 || Unsafe(before) || Unsafe(after)) return null;
        return new LearnedEdit(before, after, oldWords, newWords, IsGeneral(oldWords, newWords));
    }

    /// <summary>
    /// The fix implied by a corrected word the user selected: finds the stretch of the last dictation (up to three words) that
    /// most resembles <paramref name="selection"/> and treats it as what was misheard. Null when nothing is clearly similar.
    /// </summary>
    public static LearnedEdit? FromSelection(string dictation, string selection)
    {
        selection = selection.Trim();
        var words = Tokens(dictation);
        if (words.Count == 0 || selection.Length == 0 || selection.Length > 48 || Unsafe(selection) || Tokens(selection).Count == 0) return null;
        var target = Squash(selection);

        (int First, int Last, double Score)? best = null;
        var bestHeard = "";
        var tied = false;
        for (var size = 1; size <= MaxTokensPerSide; size++)
            for (var i = 0; i + size <= words.Count; i++)
            {
                var last = i + size - 1;
                var heard = dictation[words[i].Start..words[last].End];
                if (heard == selection) return null; // the dictation already reads like this
                var squashed = Squash(heard);
                var longest = Math.Max(squashed.Length, target.Length);
                var distance = EditDistance.Levenshtein<char>(squashed.ToCharArray(), target.ToCharArray());
                var score = (double)distance / longest;
                if (score > 0.45) continue;
                if (best == null || score < best.Value.Score - 1e-9) { best = (i, last, score); bestHeard = heard; tied = false; }
                else if (Math.Abs(score - best.Value.Score) < 1e-9 && (i > best.Value.Last || last < best.Value.First)
                         && !string.Equals(heard, bestHeard, StringComparison.Ordinal)) tied = true; // different words somewhere else, just as close
            }
        if (best == null || tied) return null;

        var (firstWord, lastWord, _) = best.Value;
        var oldWords = dictation[words[firstWord].Start..words[lastWord].End];
        var firstOld = Math.Max(0, firstWord - 1);
        var lastOld = Math.Min(words.Count - 1, lastWord + 1);
        var before = dictation[words[firstOld].Start..words[lastOld].End];
        var after = dictation[words[firstOld].Start..words[firstWord].Start] + selection + dictation[words[lastWord].End..words[lastOld].End];
        if (before.Length > 90 || after.Length > 90 || Unsafe(before) || Unsafe(oldWords)) return null;
        return new LearnedEdit(before, after, oldWords, selection, IsGeneral(oldWords, selection));
    }

    /// <summary>Lower-case letters and digits only, so "sri kanth" and "Srikanth" compare as the same sound-alike.</summary>
    private static string Squash(string text) =>
        string.Concat(Tokens(text).Select(t => t.Text.ToLowerInvariant().Replace("'", "").Replace("’", "")));

    /// <summary>The words of <paramref name="text"/> in lower case, one space apart.</summary>
    internal static string Normalize(string text) => string.Join(' ', Tokens(text).Select(t => t.Text.ToLowerInvariant()));

    private static bool Unsafe(string text) => text.Contains('\n') || text.Contains("=>", StringComparison.Ordinal);

    /// <summary>Whether a fix should apply everywhere. Swapping one ordinary word for another is only right in context.</summary>
    private static bool IsGeneral(string oldWords, string newWords)
    {
        var oldTokens = Tokens(oldWords);
        var newTokens = Tokens(newWords);
        if (oldTokens.All(t => Common.Contains(t.Text.ToLowerInvariant()))) return false;
        if (string.Equals(oldWords, newWords, StringComparison.OrdinalIgnoreCase)) return true; // capitalisation: quarkus -> Quarkus
        if (oldTokens.Count != newTokens.Count) return true;                                    // split or merged: sri kanth -> Srikanth
        for (var i = 0; i < newWords.Length; i++)
        {
            var c = newWords[i];
            if (char.IsDigit(c) || c == '-' || c == '.' || (i > 0 && char.IsUpper(c) && char.IsLetter(newWords[i - 1]))) return true;
        }
        // A name-like capital where the recognizer wrote lower case.
        return char.IsUpper(newWords[0]) && !char.IsUpper(oldWords[0]);
    }

    private static List<Token> Tokens(string text) =>
        Word.Matches(text).Select(m => new Token(m.Value, m.Index, m.Index + m.Length)).ToList();
}

/// <summary>Turns a learned fix into a dictionary line, or says why it should not be added.</summary>
public static class DictionaryRules
{
    private const int MaxDictionaryChars = 8000;

    public sealed record LearnResult(string? Line, string Message);

    public static LearnResult TryLearn(string currentDictionaryText, LearnedEdit edit)
    {
        var (phrase, spelling) = edit.General ? (edit.OldWord, edit.NewWord) : (edit.Before, edit.After);
        var applied = Dictionary.Parse(currentDictionaryText).Apply(phrase);
        if (applied == spelling) return new LearnResult(null, $"Already remembered: {phrase} → {spelling}");
        if (applied != phrase) return new LearnResult(null, "This phrase already has a rule. Edit it in the dictionary.");
        var line = $"{phrase} => {spelling}";
        if (currentDictionaryText.Length + 1 + line.Length > MaxDictionaryChars)
            return new LearnResult(null, "The personal dictionary is full. Remove a rule from it first.");
        return new LearnResult(line, $"Remembered: {phrase} → {spelling}");
    }

    /// <summary>
    /// Whether the words the user changed are (part of) what a dictionary rule wrote, or contain it: the person is fixing the
    /// rule's own output, so the rule is wrong.
    /// </summary>
    public static bool Touches(string changedWords, string preferred)
    {
        var changed = " " + CorrectionLearning.Normalize(changedWords) + " ";
        var written = " " + CorrectionLearning.Normalize(preferred) + " ";
        if (changed.Trim().Length == 0 || written.Trim().Length == 0) return false;
        return written.Contains(changed, StringComparison.Ordinal) || changed.Contains(written, StringComparison.Ordinal);
    }

    /// <summary>The "heard => preferred" parts of a dictionary line, or null for anything else.</summary>
    public static (string Heard, string Preferred)? Split(string line)
    {
        var at = line.IndexOf("=>", StringComparison.Ordinal);
        return at <= 0 ? null : (line[..at].Trim(), line[(at + 2)..].Trim());
    }

    /// <summary>Removes one exact copy of each added line, leaving every other edit to the dictionary alone.</summary>
    public static string RemoveLines(string current, IEnumerable<string> additions)
    {
        var lines = current.Split('\n').ToList();
        foreach (var addition in additions)
        {
            var index = lines.FindLastIndex(l => l.TrimEnd('\r') == addition);
            if (index >= 0) lines.RemoveAt(index);
        }
        return string.Join('\n', lines);
    }
}
