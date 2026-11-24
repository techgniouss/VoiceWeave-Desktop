using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class AudioChunkerTests
{
    private static float[] Audio(int seconds, Func<int, float> sample) =>
        Enumerable.Range(0, seconds * 16000).Select(sample).ToArray();

    [Fact]
    public void ShortAudio_IsASingleChunk()
    {
        var a = Audio(10, i => 0.1f);
        var chunks = AudioChunker.Split(a, 16000, maxSeconds: 30);
        Assert.Single(chunks);
        Assert.Equal(a.Length, chunks[0].Length);
    }

    [Fact]
    public void LongAudio_SplitsWithoutLosingOrReorderingSamples()
    {
        var a = Audio(65, i => (float)(i % 1000));
        var chunks = AudioChunker.Split(a, 16000, maxSeconds: 30);
        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, c => Assert.True(c.Length <= 30 * 16000));
        Assert.Equal(a, chunks.SelectMany(c => c).ToArray());
    }

    [Fact]
    public void SplitPrefersTheQuietestPointNearTheLimit()
    {
        // loud everywhere except a silent gap at 27.0-27.5 s
        var a = Audio(40, i => i is >= 27 * 16000 and < 27 * 16000 + 8000 ? 0f : 0.5f);
        var chunks = AudioChunker.Split(a, 16000, maxSeconds: 30);
        var firstLen = chunks[0].Length;
        Assert.InRange(firstLen, 27 * 16000, 27 * 16000 + 8000);
    }

    [Fact]
    public void EmptyAudio_NoChunks() => Assert.Empty(AudioChunker.Split(Array.Empty<float>(), 16000, 30));
}
