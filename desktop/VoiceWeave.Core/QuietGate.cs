namespace VoiceWeave.Core;

/// <summary>
/// Lets audio through to a listener only while there is sound, so a quiet room costs almost nothing. When sound returns after a
/// quiet stretch it first passes the last few chunks heard (the start of the word that woke it) and asks for a fresh start.
/// </summary>
public sealed class QuietGate(float level = 0.004f, int hangoverSamples = 19_200, int leadChunks = 12)
{
    private readonly Queue<float[]> _lead = new();
    private int _quiet = int.MaxValue / 2;
    private bool _closed = true;

    /// <param name="chunk">The next piece of audio.</param>
    /// <param name="restart">True when the listener should forget what it heard before this audio.</param>
    /// <returns>The audio to pass on now: empty while the gate is closed.</returns>
    public IReadOnlyList<float[]> Process(float[] chunk, out bool restart)
    {
        restart = false;
        if (Rms(chunk) >= level) _quiet = 0; else _quiet = (int)Math.Min((long)_quiet + chunk.Length, int.MaxValue / 2);

        if (_quiet > hangoverSamples)
        {
            _closed = true;
            _lead.Enqueue(chunk);
            while (_lead.Count > leadChunks) _lead.Dequeue();
            return Array.Empty<float[]>();
        }

        if (!_closed) return new[] { chunk };
        _closed = false;
        restart = true;
        var passed = _lead.ToList(); // the audio just before the sound, then this chunk
        _lead.Clear();
        passed.Add(chunk);
        return passed;
    }

    /// <summary>Forgets everything (a dictation took the microphone for a while).</summary>
    public void Reset()
    {
        _lead.Clear();
        _quiet = int.MaxValue / 2;
        _closed = true;
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }
}
