namespace VoiceWeave.Core.Tests;

public class PauseSplitterTests
{
    private const int Rate = 16000;

    private static float[] Tone(double seconds, float amp = 0.3f) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amp * MathF.Sin(2 * MathF.PI * 220 * i / Rate)).ToArray();

    private static float[] Silence(double seconds, float noise = 0f)
    {
        var rng = new Random(1);
        return Enumerable.Range(0, (int)(seconds * Rate)).Select(_ => noise * (float)(rng.NextDouble() * 2 - 1)).ToArray();
    }

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void LongPause_SplitsIntoTwoSegments()
    {
        var audio = Join(Tone(1.5), Silence(0.9), Tone(1.5));
        var parts = PauseSplitter.Split(audio, Rate);
        Assert.Equal(2, parts.Count);
        Assert.InRange(parts[0].Length / (double)Rate, 1.5, 2.0);
        Assert.InRange(parts[1].Length / (double)Rate, 1.5, 2.0);
    }

    [Fact]
    public void ShortGap_StaysOneSegment()
    {
        var audio = Join(Tone(1.5), Silence(0.3), Tone(1.5));
        Assert.Single(PauseSplitter.Split(audio, Rate));
    }

    [Fact]
    public void BackgroundNoise_DoesNotHideThePause()
    {
        var audio = Join(Tone(1.2), Silence(1.0, noise: 0.004f), Tone(1.2));
        Assert.Equal(2, PauseSplitter.Split(audio, Rate).Count);
    }

    [Fact]
    public void LeadingAndTrailingSilence_AreTrimmed()
    {
        var audio = Join(Silence(2.0), Tone(1.0), Silence(2.0));
        var parts = PauseSplitter.Split(audio, Rate);
        Assert.Single(parts);
        Assert.True(parts[0].Length < 1.6 * Rate);
    }

    [Fact]
    public void ThreeUtterances_GiveThreeSegments_InOrder()
    {
        var audio = Join(Tone(1.0, 0.2f), Silence(1.0), Tone(1.0, 0.4f), Silence(1.0), Tone(1.0, 0.6f));
        var parts = PauseSplitter.Split(audio, Rate);
        Assert.Equal(3, parts.Count);
        Assert.True(parts[0].Max() < parts[1].Max() && parts[1].Max() < parts[2].Max());
    }

    [Fact]
    public void BlipTooShortToBeSpeech_IsDropped()
    {
        var audio = Join(Tone(1.5), Silence(1.0), Tone(0.1), Silence(1.0), Tone(1.5));
        Assert.Equal(2, PauseSplitter.Split(audio, Rate).Count);
    }

    [Fact]
    public void NoSpeechAtAll_ReturnsTheAudioUnchanged()
    {
        var audio = Silence(2.0);
        var parts = PauseSplitter.Split(audio, Rate);
        Assert.Single(parts);
        Assert.Equal(audio.Length, parts[0].Length);
    }

    [Fact]
    public void EmptyAudio_NoSegments() => Assert.Empty(PauseSplitter.Split(Array.Empty<float>(), Rate));
}
