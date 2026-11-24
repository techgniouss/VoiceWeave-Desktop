using System.Diagnostics;
using System.Net.Http;
using VoiceWeave.Core;
using VoiceWeave.Engine;
using CoreDictionary = VoiceWeave.Core.Dictionary;

/// <summary>
/// Runs a long, mixed-language recording through the real pipeline twice: fed like a live microphone (sentences are
/// recognised while it plays) and all at once (everything waits for the end), and compares the wait after the last word.
/// Usage: auto &lt;modelsDir&gt; &lt;mode: en|te|hi|ta|en+te|en+hi|en+ta&gt; &lt;wav&gt; [&lt;wav&gt; ...]
/// </summary>
static class AutoCheck
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("usage: auto <modelsDir> <mode> <wav> [<wav> ...]"); return 1; }
        var mode = SpeechModes.Parse(args[2]);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var models = new ModelManager(http);
        using var engines = new EngineManager(models, args[1], LanguagePacks.For);
        using var lid = new LanguageIdManager(models, args[1], WhisperLanguageId.Files, d => new WhisperLanguageId(d));

        var sw = Stopwatch.StartNew();
        foreach (var language in SpeechModes.Needs(mode)) await engines.EnsureReadyAsync(language);
        if (SpeechModes.IsAuto(mode)) await lid.EnsureReadyAsync();
        Console.WriteLine($"{SpeechModes.DisplayName(mode)} loaded in {sw.Elapsed.TotalSeconds:F1}s");

        var cleaner = new LlamaCleaner(args[1]);
        var router = new SpeechRouter(engines.Get, () => lid.Current, mode);
        router.Trace = line => Console.WriteLine($"  [{DateTime.Now:HH:mm:ss.fff}] {line}");
        var pipeline = new DictationPipeline(router, new CleanupService(cleaner), CoreDictionary.Parse("హైదరాబాద్ => Hyderabad")) { RomanOutput = () => true };

        var parts = new List<float[]>();
        foreach (var file in args.Skip(3))
        {
            if (parts.Count > 0) parts.Add(new float[16000]); // a one-second pause between clips
            parts.Add(ReadWav(file));
        }
        var audio = parts.SelectMany(p => p).ToArray();
        Console.WriteLine($"Audio: {audio.Length / 16000.0:F1}s from {args.Length - 3} clip(s)");

        // 1) all at once: nothing happened while "speaking", so the whole job is left after the last word
        sw.Restart();
        var bulk = pipeline.Run(audio, Tone.Casual);
        var bulkWait = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"All at once   : {bulkWait:F1}s after the last word (recognition {bulk.AsrTime.TotalSeconds:F1}s, cleanup {bulk.CleanupTime.TotalSeconds:F1}s)");
        Console.WriteLine($"  text: {bulk.Final}");

        // 2) like a live microphone: pieces of 100 ms at real speed
        var session = pipeline.StartSession(Tone.Casual);
        for (var i = 0; i < audio.Length; i += 1600)
        {
            session.Feed(audio[i..Math.Min(audio.Length, i + 1600)]);
            await Task.Delay(100);
        }
        sw.Restart();
        var live = session.Finish();
        var liveWait = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"Live (as spoken): {liveWait:F1}s after the last word (recognition {live.AsrTime.TotalSeconds:F1}s, cleanup {live.CleanupTime.TotalSeconds:F1}s)");
        Console.WriteLine($"  text: {live.Final}");
        Console.WriteLine($"\nWaiting time cut from {bulkWait:F1}s to {liveWait:F1}s");
        return 0;
    }

    private static float[] ReadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var n = (bytes.Length - 44) / 2;
        var f = new float[n];
        for (var i = 0; i < n; i++) f[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
        return f;
    }
}
