namespace VoiceWeave.Core;

/// <summary>
/// Finds pauses in audio as it arrives, so each finished stretch of speech can be recognised while the person is still
/// talking. Anything spoken without a pause is cut at its quietest point near <c>maxSegmentSeconds</c>.
/// </summary>
public sealed class LiveSegmenter(int sampleRate, double minPauseSeconds = 0.6, double minSpeechSeconds = 0.25,
    double paddingSeconds = 0.15, double maxSegmentSeconds = 25)
{
    private const double AnalyzeEverySeconds = 0.25;
    private const double SearchWindowSeconds = 5;
    private const double KeepWhenSilentSeconds = 1.0;

    private readonly List<float> _buffer = new();
    private int _sinceAnalysis;

    public double BufferedSeconds => _buffer.Count / (double)sampleRate;

    /// <summary>Adds audio and returns any speech segments that have just been finished (usually none).</summary>
    public IReadOnlyList<float[]> Feed(float[] samples)
    {
        _buffer.AddRange(samples);
        _sinceAnalysis += samples.Length;
        if (_sinceAnalysis < AnalyzeEverySeconds * sampleRate) return Array.Empty<float[]>();
        _sinceAnalysis = 0;
        return Analyze();
    }

    /// <summary>Returns whatever speech is still buffered (the last words) and starts afresh.</summary>
    public IReadOnlyList<float[]> Flush()
    {
        var audio = _buffer.ToArray();
        _buffer.Clear();
        _sinceAnalysis = 0;
        if (audio.Length == 0) return Array.Empty<float[]>();
        var runs = PauseSplitter.SpeechRuns(audio, sampleRate, minPauseSeconds, minSpeechSeconds);
        return runs.Select(r => Cut(audio, r.start, r.end)).ToList();
    }

    private IReadOnlyList<float[]> Analyze()
    {
        var audio = _buffer.ToArray();
        var runs = PauseSplitter.SpeechRuns(audio, sampleRate, minPauseSeconds, minSpeechSeconds);
        var emitted = new List<float[]>();
        var consumedTo = 0;
        var pauseSamples = (int)(minPauseSeconds * sampleRate);

        foreach (var (start, end) in runs)
        {
            var closed = audio.Length - end >= pauseSamples; // enough silence after it: the speaker has stopped
            if (closed)
            {
                emitted.Add(Cut(audio, start, end));
                consumedTo = end;
            }
            else if (end - start > maxSegmentSeconds * sampleRate)
            {
                var cut = QuietestCut(audio, start, end);
                emitted.Add(Cut(audio, start, cut));
                consumedTo = cut;
            }
        }

        if (consumedTo > 0) _buffer.RemoveRange(0, consumedTo);
        else if (runs.Count == 0 && _buffer.Count > (KeepWhenSilentSeconds + minPauseSeconds) * sampleRate)
            _buffer.RemoveRange(0, _buffer.Count - (int)(KeepWhenSilentSeconds * sampleRate)); // only silence: do not grow
        return emitted;
    }

    private float[] Cut(float[] audio, int start, int end)
    {
        var pad = (int)(paddingSeconds * sampleRate);
        return audio[Math.Max(0, start - pad)..Math.Min(audio.Length, end + pad)];
    }

    /// <summary>The quietest 20 ms frame in the last few seconds of a segment that has become too long.</summary>
    private int QuietestCut(float[] audio, int start, int end)
    {
        var frame = sampleRate / 50;
        var limit = start + (int)(maxSegmentSeconds * sampleRate);
        var from = Math.Max(start + frame, limit - (int)(SearchWindowSeconds * sampleRate));
        var best = Math.Min(limit, end);
        var bestEnergy = double.MaxValue;
        for (var pos = from; pos + frame <= Math.Min(limit, end); pos += frame)
        {
            double e = 0;
            for (var i = 0; i < frame; i++) e += audio[pos + i] * audio[pos + i];
            if (e < bestEnergy) { bestEnergy = e; best = pos; }
        }
        return best;
    }
}
