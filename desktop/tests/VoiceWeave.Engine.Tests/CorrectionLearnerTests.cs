using VoiceWeave.Core;
using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class CorrectionLearnerTests
{
    private sealed class FakeField : IFieldText
    {
        public volatile string Text = "";
        public bool Throw;
        public string? Read() => Throw ? throw new InvalidOperationException("element gone") : Text;
    }

    private sealed class FakeStore : IRuleStore
    {
        private readonly object _gate = new();
        public string Content = "";
        public List<string> Appended = new();
        public string Read() { lock (_gate) return Content; }
        public void Append(string line)
        {
            lock (_gate) { Content = Content.Length == 0 ? line : Content + "\n" + line; Appended.Add(line); }
        }
        public bool Remove(string line)
        {
            lock (_gate)
            {
                var updated = VoiceWeave.Core.DictionaryRules.RemoveLines(Content, new[] { line });
                var changed = updated != Content;
                Content = updated;
                return changed;
            }
        }
    }

    private static readonly LearnerTiming Fast = new(
        InitialDelay: TimeSpan.FromMilliseconds(5), PollEvery: TimeSpan.FromMilliseconds(15),
        SettleFor: TimeSpan.FromMilliseconds(80), WatchFor: TimeSpan.FromSeconds(4));

    private static (CorrectionLearner learner, FakeField field, FakeStore store, List<string> messages) Rig(string fieldText, bool readable = true)
    {
        var field = new FakeField { Text = fieldText };
        var store = new FakeStore();
        var messages = new List<string>();
        var learner = new CorrectionLearner(() => readable ? field : null, store, Fast);
        learner.Learned += m => { lock (messages) messages.Add(m); };
        return (learner, field, store, messages);
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds = 3000) =>
        SpinWait.SpinUntil(condition, milliseconds);

    private const string Before = "Notes: please send it to sri kanth tomorrow.";
    private const string After = "Notes: please send it to Srikanth tomorrow.";
    private const string Pasted = "please send it to sri kanth tomorrow.";

    [Fact]
    public void LearnsTheFix_OnceTheTextHasSettled()
    {
        var (learner, field, store, messages) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = After;                      // the user corrects the name
        Assert.True(WaitFor(() => store.Appended.Count == 1));
        Assert.Equal("sri kanth => Srikanth", store.Appended[0]);
        Assert.Contains(messages, m => m.Contains("Srikanth"));
    }

    [Fact]
    public void WaitsWhileTheUserIsStillTyping()
    {
        var (learner, field, store, _) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(40);
        // keeps changing faster than the settle time: nothing is learned from half-typed words
        foreach (var partial in new[] { "Notes: please send it to S tomorrow.", "Notes: please send it to Sr tomorrow.", "Notes: please send it to Sri tomorrow." })
        {
            field.Text = partial;
            Thread.Sleep(30);
        }
        Assert.Empty(store.Appended);
        field.Text = After;
        Assert.True(WaitFor(() => store.Appended.Count == 1));
        Assert.Equal("sri kanth => Srikanth", store.Appended[0]);
    }

    [Fact]
    public void NoEdit_NothingLearned()
    {
        var (learner, _, store, _) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(400);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void SameFix_IsNotLearnedTwice()
    {
        var (learner, field, store, _) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = After;
        Assert.True(WaitFor(() => store.Appended.Count == 1));
        Thread.Sleep(400);
        Assert.Single(store.Appended);
    }

    [Fact]
    public void SecondFixInTheSameText_IsLearnedToo()
    {
        var (learner, field, store, _) = Rig("Use quark us with sri kanth");
        learner.Watch("Use quark us with sri kanth");
        Thread.Sleep(40);
        field.Text = "Use Quarkus with sri kanth";
        Assert.True(WaitFor(() => store.Appended.Count == 1));
        field.Text = "Use Quarkus with Srikanth";
        Assert.True(WaitFor(() => store.Appended.Count == 2));
        Assert.Equal(new[] { "quark us => Quarkus", "sri kanth => Srikanth" }, store.Appended);
    }

    [Fact]
    public void FieldThatCannotBeRead_IsIgnoredQuietly()
    {
        var (learner, _, store, _) = Rig(Before, readable: false);
        learner.Watch(Pasted);
        Thread.Sleep(300);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void ReadFailureMidWatch_StopsWithoutCrashing()
    {
        var (learner, field, store, _) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Throw = true;
        Thread.Sleep(200);
        field.Throw = false;
        field.Text = After;
        Thread.Sleep(300);
        Assert.Empty(store.Appended);           // the watch ended; nothing was learned and nothing crashed
    }

    [Fact]
    public void DictatedTextNotFoundInTheField_NothingHappens()
    {
        var (learner, field, store, _) = Rig("a totally different field");
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = "a totally different field edited";
        Thread.Sleep(300);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void Disabled_DoesNothing()
    {
        var (learner, field, store, _) = Rig(Before);
        learner.Enabled = false;
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = After;
        Thread.Sleep(300);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void ANewDictation_CancelsTheOldWatch()
    {
        var (learner, field, store, _) = Rig(Before);
        learner.Watch(Pasted);
        Thread.Sleep(40);
        learner.Cancel();
        field.Text = After;
        Thread.Sleep(300);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void ExistingRuleForThePhrase_IsNotOverwritten()
    {
        var (learner, field, store, messages) = Rig(Before);
        store.Content = "sri kanth => Shrikanth";
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = After;
        Assert.True(WaitFor(() => { lock (messages) return messages.Count >= 0; }));
        Thread.Sleep(400);
        Assert.Empty(store.Appended);
    }

    [Fact]
    public void Undo_RemovesWhatWasJustLearned()
    {
        var (learner, field, store, _) = Rig(Before);
        store.Content = "argo cd => Argo CD";
        learner.Watch(Pasted);
        Thread.Sleep(40);
        field.Text = After;
        Assert.True(WaitFor(() => store.Appended.Count == 1));
        Assert.True(learner.CanUndo);
        Assert.True(learner.UndoLast());
        Assert.Equal("argo cd => Argo CD", store.Content);
        Assert.False(learner.CanUndo);
        Assert.False(learner.UndoLast());
    }

    [Fact]
    public void WatchEndsAfterItsTimeLimit()
    {
        var field = new FakeField { Text = Before };
        var store = new FakeStore();
        var learner = new CorrectionLearner(() => field, store,
            new LearnerTiming(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(200)));
        learner.Watch(Pasted);
        Thread.Sleep(500);                       // well past the 200 ms limit
        field.Text = After;
        Thread.Sleep(300);
        Assert.Empty(store.Appended);
    }
}
