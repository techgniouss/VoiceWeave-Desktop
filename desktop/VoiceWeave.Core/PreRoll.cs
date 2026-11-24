namespace VoiceWeave.Core;

/// <summary>Fixed-size ring of the most recent audio so the first spoken word survives hotkey latency.</summary>
public sealed class PreRoll(int sampleRate, int millis)
{
    private readonly float[] _buf = new float[(int)((long)sampleRate * millis / 1000)];
    private int _head;   // next write index
    private int _count;

    public void Push(ReadOnlySpan<float> samples)
    {
        if (_buf.Length == 0) return;
        if (samples.Length >= _buf.Length)
        {
            samples[^_buf.Length..].CopyTo(_buf);
            _head = 0;
            _count = _buf.Length;
            return;
        }
        foreach (var s in samples)
        {
            _buf[_head] = s;
            _head = (_head + 1) % _buf.Length;
        }
        _count = Math.Min(_buf.Length, _count + samples.Length);
    }

    /// <summary>Returns buffered samples oldest-first and clears the buffer.</summary>
    public float[] Take()
    {
        var result = new float[_count];
        var start = (_head - _count + _buf.Length) % _buf.Length;
        for (var i = 0; i < _count; i++) result[i] = _buf[(start + i) % _buf.Length];
        _count = 0;
        _head = 0;
        return result;
    }
}
