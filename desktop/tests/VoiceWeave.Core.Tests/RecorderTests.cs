namespace VoiceWeave.Core.Tests;

public class RecorderTests
{
    private sealed class FakeAudio : IAudioSource
    {
        public event Action<float[]>? Samples;
        public bool Started, Stopped;
        public void Start() => Started = true;
        public void Stop() => Stopped = true;
        public void Emit(params float[] s) => Samples?.Invoke(s);
        public void Dispose() { }
    }

    [Fact]
    public void PreRollSamplesComeBeforeUtteranceSamples()
    {
        var src = new FakeAudio();
        var rec = new Recorder(src, new PreRoll(10, 1000)); // capacity 10 samples
        rec.Arm();
        src.Emit(1, 2, 3);
        rec.BeginUtterance();
        src.Emit(4, 5);
        Assert.Equal(new float[] { 1, 2, 3, 4, 5 }, rec.EndUtterance());
    }

    [Fact]
    public void SamplesBeforeBegin_AreOnlyKeptWithinPreRollWindow()
    {
        var src = new FakeAudio();
        var rec = new Recorder(src, new PreRoll(4, 1000)); // capacity 4
        rec.Arm();
        src.Emit(1, 2, 3, 4, 5, 6);
        rec.BeginUtterance();
        Assert.Equal(new float[] { 3, 4, 5, 6 }, rec.EndUtterance());
    }

    [Fact]
    public void EndUtterance_StopsCollecting_AndResets()
    {
        var src = new FakeAudio();
        var rec = new Recorder(src, new PreRoll(10, 1000));
        rec.Arm();
        rec.BeginUtterance();
        src.Emit(1, 2);
        Assert.Equal(new float[] { 1, 2 }, rec.EndUtterance());
        src.Emit(9);
        Assert.Empty(rec.EndUtterance());
    }

    [Fact]
    public void Cancel_DiscardsUtterance()
    {
        var src = new FakeAudio();
        var rec = new Recorder(src, new PreRoll(10, 1000));
        rec.Arm();
        rec.BeginUtterance();
        src.Emit(1, 2);
        rec.Cancel();
        Assert.Empty(rec.EndUtterance());
    }

    [Fact]
    public void Disarm_StopsSource_AndIgnoresSamples()
    {
        var src = new FakeAudio();
        var rec = new Recorder(src, new PreRoll(10, 1000));
        rec.Arm();
        rec.Disarm();
        Assert.True(src.Stopped);
        src.Emit(1);
        rec.BeginUtterance();
        Assert.Empty(rec.EndUtterance());
    }
}
