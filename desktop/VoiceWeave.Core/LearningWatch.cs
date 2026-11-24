namespace VoiceWeave.Core;

/// <summary>
/// Remembers where in a text field a dictation was pasted, using the text around it as anchors, so the dictated stretch can
/// be found again after the user has edited it. Short surrounding text must match exactly; long surrounding text is anchored by
/// its nearest characters.
/// </summary>
public sealed class LearningWatch
{
    private const int AnchorLength = 40;

    private readonly string _prefix;           // the text before the dictation (its last AnchorLength characters if long)
    private readonly bool _prefixIsWhole;
    private readonly string _suffix;
    private readonly bool _suffixIsWhole;
    private readonly int _expectedFrom;       // where the dictation began in the field when it was pasted

    private LearningWatch(string prefix, bool prefixIsWhole, string suffix, bool suffixIsWhole, string dictated, int expectedFrom)
    {
        _expectedFrom = expectedFrom;
        _prefix = prefix;
        _prefixIsWhole = prefixIsWhole;
        _suffix = suffix;
        _suffixIsWhole = suffixIsWhole;
        Dictated = dictated;
    }

    /// <summary>What the region held when it was last compared (the dictation, or the dictation as corrected so far).</summary>
    public string Dictated { get; private set; }

    /// <param name="fieldText">The whole text of the field just after the paste.</param>
    /// <param name="pasted">The text that was pasted.</param>
    /// <returns>Null when the pasted text cannot be found in the field.</returns>
    public static LearningWatch? TryCreate(string fieldText, string pasted)
    {
        if (pasted.Length == 0) return null;
        var at = fieldText.LastIndexOf(pasted, StringComparison.Ordinal);
        if (at < 0) return null;
        var before = fieldText[..at];
        var after = fieldText[(at + pasted.Length)..];
        var prefixWhole = before.Length <= AnchorLength;
        var suffixWhole = after.Length <= AnchorLength;
        return new LearningWatch(
            prefixWhole ? before : before[^AnchorLength..], prefixWhole,
            suffixWhole ? after : after[..AnchorLength], suffixWhole,
            pasted, at);
    }

    /// <summary>The dictated stretch as it reads in <paramref name="text"/> now, or null if its surroundings can no longer be found.</summary>
    public string? RegionIn(string text)
    {
        int from;
        if (_prefixIsWhole)
        {
            if (!text.StartsWith(_prefix, StringComparison.Ordinal)) return null;
            from = _prefix.Length;
        }
        else
        {
            // the anchor may occur many times; take the one where the dictation was
            var found = NearestOccurrence(text, _prefix, 0, _expectedFrom - _prefix.Length);
            if (found < 0) return null;
            from = found + _prefix.Length;
        }

        int to;
        if (_suffixIsWhole)
        {
            if (!text.EndsWith(_suffix, StringComparison.Ordinal) || text.Length - _suffix.Length < from) return null;
            to = text.Length - _suffix.Length;
        }
        else
        {
            var found = NearestOccurrence(text, _suffix, from, from + Dictated.Length);
            if (found < 0) return null;
            to = found;
        }
        return text[from..to];
    }

    private static int NearestOccurrence(string text, string anchor, int startAt, int near)
    {
        var best = -1;
        for (var i = text.IndexOf(anchor, startAt, StringComparison.Ordinal); i >= 0; i = text.IndexOf(anchor, i + 1, StringComparison.Ordinal))
            if (best < 0 || Math.Abs(i - near) < Math.Abs(best - near)) best = i;
        return best;
    }

    /// <summary>Takes the corrected text as the new baseline, so the same fix is not found twice.</summary>
    public void Rebase(string region) => Dictated = region;
}
