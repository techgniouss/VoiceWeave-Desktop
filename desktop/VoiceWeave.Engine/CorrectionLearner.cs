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
    private const int MaxPending = 200;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _pending = new(); // context-bound fixes seen so far, by how often
    private CancellationTokenSource? _current;
    private List<string> _lastLearned = new();
    private List<string> _lastForgotten = new();
    private string _lastPasted = "";

    /// <summary>How many times a fix that only makes sense in its own phrase must be made before it becomes a rule (one-off rewording is not a mistake to learn).</summary>
    public int ContextEditsNeeded { get; set; } = 2;

    /// <summary>Raised with a short message when a rule was added or dropped, for example "Remembered: sri kanth → Srikanth".</summary>
    public event Action<string>? Learned;

    public bool Enabled { get; set; } = true;

    /// <summary>Why a watch ended without learning (never contains the text), for the diagnostic log.</summary>
    public Action<string>? Trace { get; set; }

    public bool CanUndo { get { lock (_gate) return _lastLearned.Count > 0 || _lastForgotten.Count > 0; } }

    /// <summary>Starts watching the field the user is in for corrections to <paramref name="pastedText"/>, replacing any earlier watch.</summary>
    /// <param name="rules">The dictionary rules that changed the dictated text; fixing what one wrote drops that rule.</param>
    public void Watch(string pastedText, IReadOnlyList<AppliedRule>? rules = null)
    {
        if (pastedText.Length > 0) lock (_gate) _lastPasted = pastedText; // "learn the selection" works even when watching is off
        if (!Enabled || pastedText.Length == 0) return;
        CancellationTokenSource cts;
        lock (_gate)
        {
            _current?.Cancel();
            cts = _current = new CancellationTokenSource();
            _lastLearned = new List<string>();
            _lastForgotten = new List<string>();
        }
        var learnedThisWatch = new List<string>();
        var forgottenThisWatch = new List<string>();
        Trace?.Invoke("watch: started");
        _ = Task.Run(() => RunAsync(pastedText, rules ?? Array.Empty<AppliedRule>(), learnedThisWatch, forgottenThisWatch, cts.Token));
    }

    /// <summary>
    /// The user selected the word as it should have been written: finds what it replaces in the last dictation and learns the fix.
    /// </summary>
    /// <returns>A short message for the user, whether or not a rule was added.</returns>
    public string TeachSelection(string? selection)
    {
        string pasted;
        lock (_gate) pasted = _lastPasted;
        if (string.IsNullOrWhiteSpace(selection)) return "Select the corrected word first, then press Ctrl+Win+L";
        if (pasted.Length == 0) return "Nothing dictated yet to learn from";
        var edit = CorrectionLearning.FromSelection(pasted, selection);
        if (edit == null) return "Could not tell which word of the last dictation that fixes";
        var result = DictionaryRules.TryLearn(store.Read(), edit);
        if (result.Line == null) return result.Message;
        store.Append(result.Line);
        lock (_gate) _lastLearned = new List<string>(_lastLearned) { result.Line };
        Learned?.Invoke(result.Message);
        return result.Message;
    }

    /// <summary>Stops watching (the user moved on to a new dictation).</summary>
    public void Cancel()
    {
        lock (_gate) { _current?.Cancel(); _current = null; }
    }

    /// <summary>Removes the rules learned from the most recent dictation, and brings back the ones it dropped.</summary>
    public bool UndoLast()
    {
        List<string> lines, forgotten;
        lock (_gate)
        {
            lines = _lastLearned; _lastLearned = new List<string>();
            forgotten = _lastForgotten; _lastForgotten = new List<string>();
        }
        var changed = false;
        foreach (var line in lines) changed |= store.Remove(line);
        foreach (var line in forgotten) { store.Append(line); changed = true; }
        return changed;
    }

    /// <summary>Whether this fix, which only holds in its own phrase, has now been made often enough to become a rule.</summary>
    private bool Recurred(LearnedEdit edit)
    {
        if (edit.General || ContextEditsNeeded <= 1) return true;
        var key = edit.Before + "\u0001" + edit.After;
        lock (_gate)
        {
            if (_pending.Count >= MaxPending && !_pending.ContainsKey(key)) _pending.Clear();
            var seen = _pending.GetValueOrDefault(key) + 1;
            if (seen >= ContextEditsNeeded) { _pending.Remove(key); return true; }
            _pending[key] = seen;
            return false;
        }
    }

    private async Task RunAsync(string pasted, IReadOnlyList<AppliedRule> rules, List<string> learnedThisWatch, List<string> forgottenThisWatch, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timing.InitialDelay, ct);
            var field = captureFocusedField();
            var text = field?.Read();
            if (field == null || text == null) { Trace?.Invoke("watch: this field does not expose its text, so nothing can be learned here"); return; }
            var watch = LearningWatch.TryCreate(text, pasted);
            if (watch == null) { Trace?.Invoke("watch: the pasted text was not found in the field"); return; } // we cannot tell what is a correction

            var reviewed = new HashSet<LearnedEdit>();
            var lastRegion = watch.Dictated;
            var changedAt = DateTime.UtcNow;
            var deadline = DateTime.UtcNow + timing.WatchFor;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(timing.PollEvery, ct);
                text = field.Read();
                if (text == null) { Trace?.Invoke("watch: the field closed or became unreadable"); return; }
                var region = watch.RegionIn(text);
                if (region == null) { Trace?.Invoke("watch: the text around the dictation was rewritten, stopped"); return; } // stop rather than guess

                if (region != lastRegion) { lastRegion = region; changedAt = DateTime.UtcNow; continue; } // still being edited
                if (region == watch.Dictated) continue;                                                   // untouched
                if (DateTime.UtcNow - changedAt < timing.SettleFor) continue;                             // not settled yet

                foreach (var edit in CorrectionLearning.SuggestAll(watch.Dictated, region))
                {
                    if (!reviewed.Add(edit)) continue;

                    // The user is fixing what a dictionary rule wrote: that rule is wrong, so drop it instead of repeating the mistake.
                    var dropped = rules.Where(r => DictionaryRules.Touches(edit.OldWord, r.Preferred) && store.Remove(r.Line)).ToList();
                    if (dropped.Count > 0)
                    {
                        foreach (var r in dropped) forgottenThisWatch.Add(r.Line);
                        lock (_gate) { if (!ct.IsCancellationRequested) _lastForgotten = new List<string>(forgottenThisWatch); }
                        foreach (var r in dropped) Learned?.Invoke($"Forgot: {DictionaryRules.Split(r.Line)?.Heard ?? r.Line} → {r.Preferred}");
                        continue;
                    }

                    var result = DictionaryRules.TryLearn(store.Read(), edit);
                    if (result.Line == null) { Trace?.Invoke("watch: correction seen but not added (" + result.Message.Split(':')[0] + ")"); continue; }
                    if (!Recurred(edit)) { Trace?.Invoke("watch: fix only holds in its phrase, waiting to see it again"); continue; }
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
