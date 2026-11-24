namespace VoiceWeave.Engine;

/// <summary>Splits long recordings into pieces the recogniser can take, cutting at the quietest moment near the limit.</summary>
public static class AudioChunker
{
    private const int FrameMs = 20;
    private const int SearchWindowSeconds = 5;

    public static IReadOnlyList<float[]> Split(float[] audio, int sampleRate, int maxSeconds)
    {
        var chunks = new List<float[]>();
        var max = maxSeconds * sampleRate;
        var pos = 0;
        while (pos < audio.Length)
        {
            var remaining = audio.Length - pos;
            if (remaining <= max)
            {
                chunks.Add(audio[pos..]);
                break;
            }
            var cut = pos + QuietestCut(audio, pos, max, sampleRate);
            chunks.Add(audio[pos..cut]);
            pos = cut;
        }
        return chunks;
    }

    private static int QuietestCut(float[] audio, int start, int max, int sampleRate)
    {
        var frame = sampleRate * FrameMs / 1000;
        var windowStart = Math.Max(frame, max - SearchWindowSeconds * sampleRate);
        var bestOffset = max;
        var bestEnergy = double.MaxValue;
        for (var offset = windowStart; offset + frame <= max; offset += frame)
        {
            double energy = 0;
            for (var i = 0; i < frame; i++)
            {
                var s = audio[start + offset + i];
                energy += s * s;
            }
            if (energy < bestEnergy) { bestEnergy = energy; bestOffset = offset; }
        }
        return bestOffset;
    }
}
