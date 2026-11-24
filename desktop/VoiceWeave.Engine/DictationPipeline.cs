using System.Diagnostics;
using VoiceWeave.Core;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.Engine;

public sealed record PipelineResult(string Raw, string Corrected, string Final, TimeSpan AsrTime, TimeSpan CleanupTime);

/// <summary>Audio in, clean text out: speech recognition, then the personal dictionary, then cleanup.</summary>
public sealed class DictationPipeline(IAsr asr, CleanupService cleanup, CoreDictionary dictionary)
{
    /// <summary>Replaceable at runtime so edits to the dictionary file apply without a restart.</summary>
    public CoreDictionary Dictionary { get; set; } = dictionary;

    public const int SampleRate = 16000;
    private const double MinSeconds = 0.2;
    private const float SilencePeak = 0.01f;

    public PipelineResult Run(float[] audio, Tone tone)
    {
        var empty = new PipelineResult("", "", "", TimeSpan.Zero, TimeSpan.Zero);
        if (audio.Length < MinSeconds * SampleRate || Peak(audio) < SilencePeak) return empty;

        var sw = Stopwatch.StartNew();
        var raw = asr.Transcribe(audio).Trim();
        var asrTime = sw.Elapsed;
        if (raw.Length == 0) return empty with { AsrTime = asrTime };

        var corrected = Dictionary.Apply(raw);
        sw.Restart();
        var final = cleanup.Run(corrected, tone, Dictionary.Terms);
        return new PipelineResult(raw, corrected, final, asrTime, sw.Elapsed);
    }

    private static float Peak(float[] a)
    {
        var peak = 0f;
        foreach (var s in a) { var v = Math.Abs(s); if (v > peak) peak = v; }
        return peak;
    }
}
