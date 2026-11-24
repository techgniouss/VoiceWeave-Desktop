using VoiceWeave.Core;

namespace VoiceWeave.Engine;

/// <param name="InitialDelay">How long after the paste before the field is first read (apps update their text a moment later).</param>
/// <param name="PollEvery">How often the field is checked afterwards.</param>
/// <param name="SettleFor">How long the text must stay unchanged before its fixes are learned, so half-typed words are not.</param>
/// <param name="WatchFor">How long after a dictation the user's edits are still treated as corrections of it.</param>
public sealed record LearnerTiming(TimeSpan InitialDelay, TimeSpan PollEvery, TimeSpan SettleFor, TimeSpan WatchFor)
{
    public static readonly LearnerTiming Default = new(
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(90));
}

/// <summary>
/// Learns from the user's corrections: after a dictation is pasted it watches that stretch of the text box, and when the user
/// fixes a misheard word and stops typing it adds a rule to the personal dictionary, so the same mistake is not made again.
/// Only the fix (the old and new words) is kept; the surrounding text is never stored.
/// </summary>
public sealed class CorrectionLearner(Func<IFieldText?> captureFocusedField, IRuleStore store, LearnerTiming timing) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private List<string> _lastLearned = new();

    /// <summary>Raised with a short message when a rule was added, for example "Remembered: sri kanth → Srikanth".</summary>
    public event Action<string>? Learned;

    public bool Enabled { get; set; } = true;

    public bool CanUndo { get { lock (_gate) return _lastLearned.Count > 0; } }

    /// <summary>Starts watching the field the user is in for corrections to <paramref name="pastedText"/>, replacing any earlier watch.</summary>
    public void Watch(string pastedText)
    {
        if (!Enabled || pastedText.Length == 0) return;
        CancellationTokenSource cts;
        lock (_gate)
        {
            _current?.Cancel();
            cts = _current = new CancellationTokenSource();
            _lastLearned = new List<string>();
        }
        var learnedThisWatch = new List<string>();
        _ = Task.Run(() => RunAsync(pastedText, learnedThisWatch, cts.Token));
    }

    /// <summary>Stops watching (the user moved on to a new dictation).</summary>
    public void Cancel()
    {
        lock (_gate) { _current?.Cancel(); _current = null; }
    }

    /// <summary>Removes the rules learned from the most recent dictation.</summary>
    public bool UndoLast()
    {
        List<string> lines;
        lock (_gate) { lines = _lastLearned; _lastLearned = new List<string>(); }
        var removed = false;
        foreach (var line in lines) removed |= store.Remove(line);
        return removed;
    }

    private async Task RunAsync(string pasted, List<string> learnedThisWatch, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timing.InitialDelay, ct);
            var field = captureFocusedField();
            var text = field?.Read();
            if (field == null || text == null) return;
            var watch = LearningWatch.TryCreate(text, pasted);
            if (watch == null) return; // the paste cannot be found in what we can read, so we cannot tell what is a correction

            var reviewed = new HashSet<LearnedEdit>();
            var lastRegion = watch.Dictated;
            var changedAt = DateTime.UtcNow;
            var deadline = DateTime.UtcNow + timing.WatchFor;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(timing.PollEvery, ct);
                text = field.Read();
                if (text == null) return;
                var region = watch.RegionIn(text);
                if (region == null) return; // the text around the dictation was rewritten: stop rather than guess

                if (region != lastRegion) { lastRegion = region; changedAt = DateTime.UtcNow; continue; } // still being edited
                if (region == watch.Dictated) continue;                                                   // untouched
                if (DateTime.UtcNow - changedAt < timing.SettleFor) continue;                             // not settled yet

                foreach (var edit in CorrectionLearning.SuggestAll(watch.Dictated, region))
                {
                    if (!reviewed.Add(edit)) continue;
                    var result = DictionaryRules.TryLearn(store.Read(), edit);
                    if (result.Line == null) continue;
                    store.Append(result.Line);
                    learnedThisWatch.Add(result.Line);
                    lock (_gate) { if (!ct.IsCancellationRequested) _lastLearned = new List<string>(learnedThisWatch); }
                    Learned?.Invoke(result.Message);
                }
                watch.Rebase(region); // later edits are compared with the corrected text
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Learning is a convenience: a field that disappears or a file that is locked must never disturb dictation.
        }
    }

    public void Dispose() => Cancel();
}
