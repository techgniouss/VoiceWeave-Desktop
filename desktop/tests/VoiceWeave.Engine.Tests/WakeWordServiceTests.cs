using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class WakeWordServiceTests
{
    // A spotter that "hears" the phrase on the chunks hearAt says yes to (every chunk when not given).
    private sealed class FakeSpotter(Func<int, bool>? hearAt = null) : IWakeSpotter
    {
        private int _fed;
        public int Fed => _fed;
        public int Resets;
        public string Phrase = "";
        public void Reset() => Interlocked.Increment(ref Resets);
        public bool Feed(float[] samples) => hearAt?.Invoke(Interlocked.Increment(ref _fed)) ?? (Interlocked.Increment(ref _fed) > 0);
        public void SetPhrase(string phrase) => Phrase = phrase;
        public void Dispose() { }
    }

    private static float[] Voice(float amplitude, int n = 480) => Enumerable.Range(0, n).Select(i => (i % 2 == 0 ? 1f : -1f) * amplitude).ToArray();

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 2000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(10); }
        return condition();
    }

    [Fact]
    public async Task LoudPhrase_IsHeard()
    {
        using var service = new WakeWordService(new FakeSpotter()) { MinLevel = 0.04f };
        var heard = 0; var quiet = 0;
        service.Heard += () => Interlocked.Increment(ref heard);
        service.TooQuiet += _ => Interlocked.Increment(ref quiet);

        service.Feed(Voice(0.1f));

        Assert.True(await WaitFor(() => heard == 1));
        Assert.Equal(0, quiet);
    }

    [Fact]
    public async Task QuietPhrase_IsIgnored_AndReportedWithItsLevel()
    {
        using var service = new WakeWordService(new FakeSpotter()) { MinLevel = 0.04f };
        var heard = 0; float level = -1;
        service.Heard += () => Interlocked.Increment(ref heard);
        service.TooQuiet += l => level = l;

        service.Feed(Voice(0.01f)); // loud enough to pass the quiet-room gate, too quiet for the close-speech check

        Assert.True(await WaitFor(() => level >= 0));
        Assert.Equal(0.01f, level, 3);
        Assert.Equal(0, heard);
    }

    [Fact]
    public async Task MinLevelZero_AcceptsAnyLoudness()
    {
        using var service = new WakeWordService(new FakeSpotter()) { MinLevel = 0f };
        var heard = 0;
        service.Heard += () => Interlocked.Increment(ref heard);

        service.Feed(Voice(0.01f));

        Assert.True(await WaitFor(() => heard == 1));
    }

    [Fact]
    public async Task MinLevel_CanBeChangedWhileListening()
    {
        using var service = new WakeWordService(new FakeSpotter()) { MinLevel = 0.5f };
        var heard = 0; var quiet = 0;
        service.Heard += () => Interlocked.Increment(ref heard);
        service.TooQuiet += _ => Interlocked.Increment(ref quiet);

        service.Feed(Voice(0.1f));
        Assert.True(await WaitFor(() => quiet == 1));

        service.MinLevel = 0f;
        await Task.Delay(2100); // past the cooldown, which a rejected phrase must not have started
        service.Feed(Voice(0.1f));
        Assert.True(await WaitFor(() => heard == 1));
    }

    [Fact]
    public async Task RejectedPhrase_DoesNotStartTheCooldown()
    {
        using var service = new WakeWordService(new FakeSpotter()) { MinLevel = 0.5f };
        var heard = 0; var quiet = 0;
        service.Heard += () => Interlocked.Increment(ref heard);
        service.TooQuiet += _ => Interlocked.Increment(ref quiet);

        service.Feed(Voice(0.1f));
        Assert.True(await WaitFor(() => quiet == 1));
        service.MinLevel = 0f;
        service.Feed(Voice(0.1f)); // straight after: a loud phrase must not be swallowed by a cooldown from the quiet one

        Assert.True(await WaitFor(() => heard == 1));
    }

    [Fact]
    public async Task WhilePaused_NothingIsHeard()
    {
        var spotter = new FakeSpotter();
        using var service = new WakeWordService(spotter) { Paused = () => true, MinLevel = 0f };
        var heard = 0;
        service.Heard += () => Interlocked.Increment(ref heard);

        service.Feed(Voice(0.2f));
        await Task.Delay(200);

        Assert.Equal(0, heard);
        Assert.Equal(0, spotter.Fed);
    }

    [Fact]
    public async Task SilentRoom_NeverReachesTheSpotter()
    {
        var spotter = new FakeSpotter();
        using var service = new WakeWordService(spotter) { MinLevel = 0f };
        for (var i = 0; i < 50; i++) service.Feed(new float[480]);
        await Task.Delay(200);

        Assert.Equal(0, spotter.Fed);
    }

    [Fact]
    public async Task OldLoudSound_DoesNotLetALaterQuietPhraseThrough()
    {
        // A loud noise, then a long quiet stretch (more than the two seconds the loudness is remembered for), then a quiet phrase.
        var spotter = new FakeSpotter(n => n == 71); // hears the phrase on the very last chunk (kept under the 100 the service queues, so none are dropped)
        using var service = new WakeWordService(spotter) { MinLevel = 0.04f };
        var heard = 0; var quiet = 0;
        service.Heard += () => Interlocked.Increment(ref heard);
        service.TooQuiet += _ => Interlocked.Increment(ref quiet);

        service.Feed(Voice(0.5f));
        for (var i = 0; i < 70; i++) service.Feed(Voice(0.01f)); // 70 x 30 ms of quiet speech: more than the 60 pieces remembered, so the loud noise is long gone
        Assert.True(await WaitFor(() => quiet == 1));

        Assert.Equal(0, heard);
    }
}
