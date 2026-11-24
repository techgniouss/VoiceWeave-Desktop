using System.Diagnostics;
using System.Net.Http;
using VoiceWeave.Core;
using VoiceWeave.Engine;

/// <summary>Runs recorded WAV files through a regional engine and prints the text in native script and English letters.</summary>
static class RegionalCheck
{
    public static async Task<int> Run(string[] args)
    {
        // regional <modelsDir> <te|hi|ta|en> <indicDir>   (indicDir holds items.txt and NN.wav from record_indic.py)
        if (args.Length < 4) { Console.WriteLine("usage: regional <modelsDir> <te|hi|ta> <indicDir>"); return 1; }
        var language = Languages.Parse(args[2]);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var engines = new EngineManager(new ModelManager(http), args[1], LanguagePacks.For);
        var sw = Stopwatch.StartNew();
        await engines.EnsureReadyAsync(language, new Progress<double>(p => { if (p is > 0 and < 1) Console.Write($"\rdownloading {p:P0}  "); }));
        Console.WriteLine($"{Languages.DisplayName(language)} ready in {sw.Elapsed.TotalSeconds:F1}s");
        var asr = engines.Get(language);

        var items = File.ReadAllLines(Path.Combine(args[3], "items.txt"));
        for (var i = 0; i < items.Length; i++)
        {
            var parts = items[i].Split('\t');
            if (Languages.Parse(parts[0]) != language) continue;
            var file = Path.Combine(args[3], $"{i:00}.wav");
            var audio = ReadWav16kMono(file);
            sw.Restart();
            var text = asr.Transcribe(audio);
            var t = sw.Elapsed.TotalSeconds;
            Console.WriteLine($"#{i} {audio.Length / 16000.0:F1}s decoded in {t:F2}s");
            Console.WriteLine($"   said   : {parts[1]}");
            Console.WriteLine($"   native : {text}");
            Console.WriteLine($"   roman  : {Romanizer.Convert(text)}");
        }
        return 0;
    }

    private static float[] ReadWav16kMono(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var data = 44; // plain PCM header written by record_indic.py
        var n = (bytes.Length - data) / 2;
        var f = new float[n];
        for (var i = 0; i < n; i++) f[i] = BitConverter.ToInt16(bytes, data + i * 2) / 32768f;
        return f;
    }
}
