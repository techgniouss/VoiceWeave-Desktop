using VoiceWeave.Core;
using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class DictationControllerTests
{
    private sealed class FakeAudio : IAudioSource
    {
        public event Action<float[]>? Samples;
        public void Start() { }
        public void Stop() { }
        public void Emit(int n, float v = 0.3f) => Samples?.Invoke(Enumerable.Repeat(v, n).ToArray());
        public void Dispose() { }
    }

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text;
        public readonly List<string> Pasted = new();
        public ClipSnapshot Snapshot() => new(new Dictionary<uint, byte[]>());
        public void SetText(string t) => Text = t;
        public string? ReadText() => Text;
        public void Restore(ClipSnapshot s) { }
    }

    private sealed class FakePaster(FakeClipboard cb) : IPaster
    {
        public void CtrlV(bool terminal) => cb.Pasted.Add(cb.Text ?? "");
    }

    private sealed class Rig
    {
        public readonly FakeAudio Audio = new();
        public readonly FakeClipboard Clip = new();
        public readonly List<string> Messages = new();
        public readonly List<DictationState> States = new();
        public TaskCompletionSource Ready = new();
        public Func<float[], PipelineResult> Run = a => new PipelineResult("raw", "raw", "Hello there.", default, default);
        public FocusInfo Focus = new(false, false, false, "");
        public int RunCalls;
        public readonly DictationController Controller;

        public Rig(bool ready = true)
        {
            if (ready) Ready.SetResult();
            var recorder = new Recorder(Audio, new PreRoll(16000, 500));
            recorder.Arm();
            Controller = new DictationController(recorder,
                a => { Interlocked.Increment(ref RunCalls); return Run(a); },
                Clip, new FakePaster(Clip), () => Focus, Ready.Task, TimeSpan.Zero);
            Controller.Message += m => Messages.Add(m);
            Controller.StateChanged += s => States.Add(s);
        }

        public async Task Speak(Action<DictationController> start, Action<DictationController> end, int samples = 16000)
        {
            start(Controller);
            Audio.Emit(samples);
            end(Controller);
            await Controller.WaitUntilIdleAsync();
        }
    }

    [Fact]
    public async Task Hold_ThenFinish_PastesCleanText()
    {
        var rig = new Rig();
        await rig.Speak(c => c.Begin(latched: false), c => c.Finish());
        Assert.Equal(new[] { "Hello there." }, rig.Clip.Pasted);
        Assert.Equal(DictationState.Idle, rig.Controller.State);
        Assert.Contains(DictationState.Recording, rig.States);
        Assert.Contains(DictationState.Processing, rig.States);
    }

    [Fact]
    public async Task ClickLatched_RecordsUntilFinish()
    {
        var rig = new Rig();
        rig.Controller.Begin(latched: true);
        Assert.True(rig.Controller.IsLatched);
        rig.Audio.Emit(16000);
        rig.Controller.Finish();
        await rig.Controller.WaitUntilIdleAsync();
        Assert.Single(rig.Clip.Pasted);
    }

    [Fact]
    public async Task Latch_PromotesAHoldToHandsFree()
    {
        var rig = new Rig();
        rig.Controller.Begin(latched: false);
        Assert.False(rig.Controller.IsLatched);
        rig.Controller.Latch();
        Assert.True(rig.Controller.IsLatched);
        rig.Controller.Cancel();
        await rig.Controller.WaitUntilIdleAsync();
    }

    [Fact]
    public async Task Cancel_PastesNothing_AndNeverRunsTheModel()
    {
        var rig = new Rig();
        await rig.Speak(c => c.Begin(false), c => c.Cancel());
        Assert.Empty(rig.Clip.Pasted);
        Assert.Equal(0, rig.RunCalls);
        Assert.Equal(DictationState.Idle, rig.Controller.State);
    }

    [Fact]
    public async Task FinishWhileIdle_DoesNothing()
    {
        var rig = new Rig();
        rig.Controller.Finish();
        await rig.Controller.WaitUntilIdleAsync();
        Assert.Equal(0, rig.RunCalls);
    }

    [Fact]
    public async Task BeginWhileAlreadyRecording_IsIgnored()
    {
        var rig = new Rig();
        rig.Controller.Begin(false);
        rig.Audio.Emit(16000);
        rig.Controller.Begin(true);
        Assert.False(rig.Controller.IsLatched);
        rig.Controller.Finish();
        await rig.Controller.WaitUntilIdleAsync();
        Assert.Single(rig.Clip.Pasted);
    }

    [Fact]
    public async Task UtteranceDuringModelLoading_IsQueued_ThenPastedOnce()
    {
        var rig = new Rig(ready: false);
        rig.Controller.Begin(false);
        rig.Audio.Emit(16000);
        rig.Controller.Finish();
        await Task.Delay(100);
        Assert.Empty(rig.Clip.Pasted);
        Assert.Equal(DictationState.Processing, rig.Controller.State);
        rig.Ready.SetResult();
        await rig.Controller.WaitUntilIdleAsync();
        Assert.Single(rig.Clip.Pasted);
    }

    [Fact]
    public async Task TwoUtterances_ArePastedInOrder()
    {
        var rig = new Rig();
        var n = 0;
        rig.Run = _ => new PipelineResult("r", "r", $"Text {++n}.", default, default);
        for (var i = 0; i < 2; i++)
        {
            rig.Controller.Begin(false);
            rig.Audio.Emit(16000);
            rig.Controller.Finish();
        }
        await rig.Controller.WaitUntilIdleAsync();
        Assert.Equal(new[] { "Text 1.", "Text 2." }, rig.Clip.Pasted);
    }

    [Fact]
    public async Task NothingHeard_ShowsMessage_NoPaste()
    {
        var rig = new Rig();
        rig.Run = _ => new PipelineResult("", "", "", default, default);
        await rig.Speak(c => c.Begin(false), c => c.Finish());
        Assert.Empty(rig.Clip.Pasted);
        Assert.Contains(rig.Messages, m => m.Contains("catch"));
    }

    [Fact]
    public async Task PasswordField_ShowsMessage_NoPaste()
    {
        var rig = new Rig { Focus = new FocusInfo(true, false, false, "") };
        await rig.Speak(c => c.Begin(false), c => c.Finish());
        Assert.Empty(rig.Clip.Pasted);
        Assert.Contains(rig.Messages, m => m.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ElevatedWindow_ShowsMessage_NoPaste()
    {
        var rig = new Rig { Focus = new FocusInfo(false, true, false, "") };
        await rig.Speak(c => c.Begin(false), c => c.Finish());
        Assert.Empty(rig.Clip.Pasted);
        Assert.Contains(rig.Messages, m => m.Contains("admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PipelineThrows_ShowsError_AndNextUtteranceStillWorks()
    {
        var rig = new Rig();
        var calls = 0;
        rig.Run = _ => ++calls == 1
            ? throw new InvalidOperationException("boom")
            : new PipelineResult("r", "r", "Worked.", default, default);
        await rig.Speak(c => c.Begin(false), c => c.Finish());
        Assert.Contains(rig.Messages, m => m.Contains("wrong", StringComparison.OrdinalIgnoreCase));
        await rig.Speak(c => c.Begin(false), c => c.Finish());
        Assert.Equal(new[] { "Worked." }, rig.Clip.Pasted);
    }

    [Fact]
    public async Task Level_ReportedWhileRecording()
    {
        var rig = new Rig();
        var levels = new List<float>();
        rig.Controller.Level += l => levels.Add(l);
        rig.Controller.Begin(false);
        rig.Controller.PushLevel(new float[] { 0.5f, -0.5f, 0.5f, -0.5f });
        Assert.Single(levels);
        Assert.InRange(levels[0], 0.45f, 0.55f);
        rig.Controller.Cancel();
        await rig.Controller.WaitUntilIdleAsync();
    }
}
