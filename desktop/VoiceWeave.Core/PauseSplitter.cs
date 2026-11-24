namespace VoiceWeave.Core;

/// <summary>
/// Cuts a recording at its pauses so each stretch of speech can be recognised on its own (for example an English
/// sentence, a pause, then a Telugu one). Uses short-frame energy against a threshold that adapts to the room noise.
/// </summary>
public static class PauseSplitter
{
    private const double FrameSeconds = 0.02;

    public static IReadOnlyList<float[]> Split(float[] audio, int sampleRate,
        double minPauseSeconds = 0.55, double minSpeechSeconds = 0.25, double paddingSeconds = 0.15)
    {
        if (audio.Length == 0) return Array.Empty<float[]>();
        var runs = SpeechRuns(audio, sampleRate, minPauseSeconds, minSpeechSeconds);
        if (runs.Count == 0) return new[] { audio };

        var pad = (int)(paddingSeconds * sampleRate);
        return runs.Select(r => audio[Math.Max(0, r.start - pad)..Math.Min(audio.Length, r.end + pad)]).ToList();
    }

    /// <summary>
    /// Sample ranges of speech, with pauses shorter than <paramref name="minPauseSeconds"/> bridged and blips shorter than
    /// <paramref name="minSpeechSeconds"/> dropped. The loudness threshold adapts to the room noise.
    /// </summary>
    internal static IReadOnlyList<(int start, int end)> SpeechRuns(float[] audio, int sampleRate, double minPauseSeconds, double minSpeechSeconds)
    {
        var frame = Math.Max(1, (int)(sampleRate * FrameSeconds));
        var frames = audio.Length / frame;
        if (frames == 0) return Array.Empty<(int, int)>();

        var rms = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var i = f * frame; i < (f + 1) * frame; i++) sum += audio[i] * audio[i];
            rms[f] = Math.Sqrt(sum / frame);
        }

        var sorted = rms.OrderBy(x => x).ToArray();
        var floor = sorted[(int)(sorted.Length * 0.10)];
        var loud = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.95))];
        // When there is no quiet stretch at all (continuous speech) the "floor" is really speech, so never let it
        // push the threshold above a fraction of the loudest speech.
        var threshold = Math.Max(Math.Max(Math.Min(floor * 3.0, loud * 0.3), loud * 0.08), 0.004);

        var runs = new List<(int start, int end)>();
        var begin = -1;
        for (var f = 0; f < frames; f++)
        {
            if (rms[f] > threshold) { if (begin < 0) begin = f; }
            else if (begin >= 0) { runs.Add((begin, f)); begin = -1; }
        }
        if (begin >= 0) runs.Add((begin, frames));
        if (runs.Count == 0) return Array.Empty<(int, int)>();

        var minPauseFrames = (int)Math.Ceiling(minPauseSeconds / FrameSeconds);
        var merged = new List<(int start, int end)> { runs[0] };
        foreach (var r in runs.Skip(1))
        {
            var last = merged[^1];
            if (r.start - last.end < minPauseFrames) merged[^1] = (last.start, r.end);
            else merged.Add(r);
        }

        var minSpeechFrames = (int)Math.Ceiling(minSpeechSeconds / FrameSeconds);
        return merged.Where(r => r.end - r.start >= minSpeechFrames).Select(r => (r.start * frame, r.end * frame)).ToList();
    }
}
