namespace VoiceWeave.Core.Tests;

public class LiveSegmenterTests
{
    private const int Rate = 16000;

    private static float[] Tone(double seconds, float amp = 0.3f) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amp * MathF.Sin(2 * MathF.PI * 220 * i / Rate)).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>Feeds audio in 100 ms pieces, like a microphone, and returns what was emitted while feeding.</summary>
    private static List<float[]> FeedAll(LiveSegmenter s, float[] audio)
    {
        var emitted = new List<float[]>();
        for (var i = 0; i < audio.Length; i += Rate / 10)
            emitted.AddRange(s.Feed(audio[i..Math.Min(audio.Length, i + Rate / 10)]));
        return emitted;
    }

    [Fact]
    public void FinishedUtterance_IsEmittedWhileTheNextOneIsStillBeingSpoken()
    {
        var s = new LiveSegmenter(Rate);
        var emitted = FeedAll(s, Join(Tone(1.5), Silence(0.9), Tone(1.5)));
        Assert.Single(emitted);                    // the first is ready before the user finishes
        var tail = s.Flush();
        Assert.Single(tail);                       // the second comes out at the end
        Assert.InRange(emitted[0].Length / (double)Rate, 1.5, 2.1);
    }

    [Fact]
    public void ShortGap_DoesNotEmitEarly()
    {
        var s = new LiveSegmenter(Rate);
        var emitted = FeedAll(s, Join(Tone(1.5), Silence(0.3), Tone(1.5)));
        Assert.Empty(emitted);
        Assert.Single(s.Flush());
    }

    [Fact]
    public void ContinuousSpeech_IsCutIntoBoundedPieces()
    {
        var s = new LiveSegmenter(Rate, maxSegmentSeconds: 25);
        var emitted = FeedAll(s, Tone(60));
        var tail = s.Flush();
        var all = emitted.Concat(tail).ToList();
        Assert.True(emitted.Count >= 2);
        Assert.All(all, seg => Assert.True(seg.Length <= 26 * Rate));
        Assert.True(all.Sum(seg => seg.Length) >= 59.5 * Rate); // nothing lost
    }

    [Fact]
    public void Silence_ProducesNothing_AndKeepsMemoryBounded()
    {
        var s = new LiveSegmenter(Rate);
        Assert.Empty(FeedAll(s, Silence(120)));
        Assert.Empty(s.Flush());
        Assert.True(s.BufferedSeconds < 5);
    }

    [Fact]
    public void LongDialogue_EmitsEverySentenceInOrder()
    {
        var s = new LiveSegmenter(Rate);
        var audio = Join(Tone(1, 0.2f), Silence(0.8), Tone(1, 0.3f), Silence(0.8), Tone(1, 0.4f), Silence(0.8), Tone(1, 0.5f));
        var all = FeedAll(s, audio).Concat(s.Flush()).ToList();
        Assert.Equal(4, all.Count);
        Assert.True(all[0].Max() < all[1].Max() && all[1].Max() < all[2].Max() && all[2].Max() < all[3].Max());
    }

    [Fact]
    public void FlushResetsTheSegmenter()
    {
        var s = new LiveSegmenter(Rate);
        FeedAll(s, Tone(1.5));
        s.Flush();
        Assert.Empty(s.Flush());
        var again = FeedAll(s, Join(Tone(1.2), Silence(0.9), Tone(1.2)));
        Assert.Single(again);
    }

    [Fact]
    public void LeadingSilence_IsNotPartOfTheFirstSegment()
    {
        var s = new LiveSegmenter(Rate);
        var all = FeedAll(s, Join(Silence(3), Tone(1.0))).Concat(s.Flush()).ToList();
        Assert.Single(all);
        Assert.True(all[0].Length < 1.6 * Rate);
    }
}
