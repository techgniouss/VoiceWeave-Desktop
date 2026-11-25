using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

/// <summary>Compares settings of the cleanup model for speed (and prints the output so quality can be compared by eye).</summary>
static class CleanupBench
{
    private static readonly string[] Samples =
    {
        "um so i think we should meet on friday at three pm",
        "send the report to john no wait to maria by friday",
        "can you check the build",
        "yeah that works for me thanks",
        "i'll be there in ten minutes",
        "uh hello can you hear me",
        "we need about two hundred and fifty users by the end of the quarter and uh i think that is realistic",
        "so basically the thing is that you know the deploy failed because the config was wrong and i mean we need to fix it before monday morning or else the whole release slips",
    };

    private sealed record Variant(string Name, uint Context, int Threads, int? BatchThreads, bool Greedy, bool Flash, bool AdaptiveMax);

    public static async Task Run(string modelPath)
    {
        var variants = new[]
        {
            new Variant("baseline ctx1024 t4 sampled", 1024, 4, null, false, false, false),
            new Variant("greedy ctx512 t4 adaptive", 512, 4, null, true, false, true),
            new Variant("greedy ctx512 t4 flash", 512, 4, null, true, true, true),
            new Variant("greedy ctx512 t6", 512, 6, null, true, false, true),
            new Variant("greedy ctx512 t4 batch8", 512, 4, 8, true, false, true),
            new Variant("greedy ctx512 t8", 512, 8, null, true, false, true),
        };
        foreach (var v in variants)
        {
            var p = new ModelParams(modelPath) { ContextSize = v.Context, GpuLayerCount = 0, Threads = v.Threads, FlashAttention = v.Flash };
            if (v.BatchThreads is int bt) p.BatchThreads = bt;
            using var weights = LLamaWeights.LoadFromFile(p);
            var executor = new StatelessExecutor(weights, p);
            Console.WriteLine($"== {v.Name}");
            await Once(executor, v, Samples[0]); // warm-up, not reported
            double total = 0;
            foreach (var text in Samples)
            {
                var (ms, output) = await Once(executor, v, text);
                total += ms;
                Console.WriteLine($"{ms,6:F0} ms | {output}");
            }
            Console.WriteLine($"   total {total:F0} ms\n");
        }
    }

    private static async Task<(double ms, string output)> Once(StatelessExecutor executor, Variant v, string text)
    {
        var infer = new InferenceParams { MaxTokens = v.AdaptiveMax ? Math.Min(160, text.Length / 3 + 12) : 160, AntiPrompts = new[] { "\n" } };
        if (v.Greedy) infer.SamplingPipeline = new GreedySamplingPipeline();
        var sw = Stopwatch.StartNew();
        var sb = new StringBuilder();
        await foreach (var piece in executor.InferAsync($"tone: casual\ntext: {text}\nout:\n", infer)) sb.Append(piece);
        return (sw.Elapsed.TotalMilliseconds, sb.ToString().Trim());
    }
}
