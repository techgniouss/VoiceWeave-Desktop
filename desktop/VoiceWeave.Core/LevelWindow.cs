namespace VoiceWeave.Core;

/// <summary>
/// Remembers how loud the last stretch of audio was (the loudest short piece in it), so a heard wake phrase can be checked
/// against how close the speaker was: someone at the microphone is much louder than a TV or a conversation across the room.
/// </summary>
public sealed class LevelWindow(int pieces = 60)
{
    private readonly object _gate = new();
    private readonly float[] _ring = new float[pieces];
    private int _next;

    /// <summary>Records one piece of audio (about 30 ms): its loudness as the root mean square.</summary>
    public void Push(float[] samples)
    {
        var rms = Rms(samples);
        lock (_gate)
        {
            _ring[_next] = rms;
            _next = (_next + 1) % _ring.Length;
        }
    }

    /// <summary>The loudest piece in the window (about the last two seconds).</summary>
    public float Peak
    {
        get
        {
            lock (_gate)
            {
                var peak = 0f;
                foreach (var level in _ring) if (level > peak) peak = level;
                return peak;
            }
        }
    }

    public void Clear()
    {
        lock (_gate) Array.Clear(_ring);
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }
}
