using System.Windows.Automation;
using System.Windows.Forms;

namespace VoiceWeave.Win.Tests;

/// <summary>A test that needs a real interactive desktop; skipped on build servers, where there may be none.</summary>
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Skip = "needs an interactive desktop (run on a developer machine)";
    }
}

/// <summary>
/// Reads real Windows text boxes through UI Automation, in a window opened off-screen and without taking the keyboard focus,
/// to prove that corrections to a dictation can actually be seen (and that a password box cannot be read).
/// </summary>
public class UiaFieldTextTests
{
    private sealed class OffscreenForm : Form
    {
        public readonly TextBox Box;

        public OffscreenForm(bool password, bool multiline)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(-32000, -32000);
            Size = new System.Drawing.Size(300, 200);
            Box = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = password, Multiline = multiline };
            Controls.Add(Box);
        }

        protected override bool ShowWithoutActivation => true;
    }

    /// <summary>Runs <paramref name="body"/> against a text box living on its own UI thread.</summary>
    private static T WithTextBox<T>(bool password, bool multiline, string initial, Func<OffscreenForm, T> body)
    {
        T? result = default;
        Exception? failure = null;
        var ready = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        OffscreenForm? form = null;
        var thread = new Thread(() =>
        {
            try
            {
                form = new OffscreenForm(password, multiline);
                form.Box.Text = initial;
                form.Shown += (_, _) => ready.Set();
                form.Show();
                ready.Set();
                while (!done.IsSet) { Application.DoEvents(); Thread.Sleep(10); }
                form.Close();
            }
            catch (Exception e) { failure = e; ready.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        Thread.Sleep(200);
        try
        {
            if (failure != null) throw failure;
            result = body(form!);
        }
        finally
        {
            done.Set();
            thread.Join(TimeSpan.FromSeconds(5));
        }
        return result!;
    }

    private static AutomationElement ElementOf(OffscreenForm form)
    {
        IntPtr handle = IntPtr.Zero;
        form.Invoke(() => handle = form.Box.Handle);
        return AutomationElement.FromHandle(handle);
    }

    [DesktopFact]
    public void ReadsTheTextOfASingleLineBox_AndSeesLaterEdits()
    {
        var (first, second) = WithTextBox(false, false, "please send it to sri kanth tomorrow.", form =>
        {
            var field = UiaFieldText.FromElement(ElementOf(form));
            Assert.NotNull(field);
            var a = field!.Read();
            form.Invoke(() => form.Box.Text = "please send it to Srikanth tomorrow.");
            Thread.Sleep(100);
            return (a, field.Read());
        });
        Assert.Equal("please send it to sri kanth tomorrow.", first);
        Assert.Equal("please send it to Srikanth tomorrow.", second);
    }

    [DesktopFact]
    public void ReadsAMultilineBox()
    {
        var text = WithTextBox(false, true, "line one\r\nline two", form => UiaFieldText.FromElement(ElementOf(form))!.Read());
        Assert.Equal("line one\r\nline two", text);
    }

    [DesktopFact]
    public void PasswordBox_IsNeverRead()
    {
        var field = WithTextBox(true, false, "hunter2", form => UiaFieldText.FromElement(ElementOf(form)));
        Assert.Null(field);
    }

    [DesktopFact]
    public void ClosedWindow_ReadsAsNull_NotAnException()
    {
        var field = WithTextBox(false, false, "hello", form => UiaFieldText.FromElement(ElementOf(form)));
        Assert.NotNull(field);
        Thread.Sleep(300); // the window is gone now
        Assert.Null(field!.Read());
    }

    [DesktopFact]
    public void WholePipeline_LearnsFromARealTextBox()
    {
        // the dictation is pasted into a real box; the user then fixes the name; the dictionary gains the rule
        var learned = WithTextBox(false, false, "Notes: please send it to sri kanth tomorrow.", form =>
        {
            var store = new MemoryStore();
            var field = UiaFieldText.FromElement(ElementOf(form));
            var learner = new VoiceWeave.Engine.CorrectionLearner(() => field, store,
                new VoiceWeave.Engine.LearnerTiming(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5)));
            learner.Watch("please send it to sri kanth tomorrow.");
            Thread.Sleep(150);
            form.Invoke(() => form.Box.Text = "Notes: please send it to Srikanth tomorrow.");
            SpinWait.SpinUntil(() => store.Lines.Count > 0, TimeSpan.FromSeconds(4));
            learner.Dispose();
            return store.Lines.ToList();
        });
        Assert.Equal(new[] { "sri kanth => Srikanth" }, learned);
    }

    private sealed class MemoryStore : VoiceWeave.Core.IRuleStore
    {
        public readonly List<string> Lines = new();
        public string Read() => string.Join("\n", Lines);
        public void Append(string line) => Lines.Add(line);
        public bool Remove(string line) => Lines.Remove(line);
    }
}
