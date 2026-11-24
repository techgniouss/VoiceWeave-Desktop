using System.Diagnostics;
using LLama;
using LLama.Common;

if (args.Length >= 2 && args[0] == "llama-probe")
{
    await LlamaProbe.Run(args[1]);
    return 0;
}
Console.WriteLine("usage: bench llama-probe <model.gguf>");
return 1;

static class LlamaProbe
{
    public static async Task Run(string modelPath)
    {
        var sw = Stopwatch.StartNew();
        var p = new ModelParams(modelPath) { ContextSize = 1024, GpuLayerCount = 0, Threads = 4 };
        using var weights = LLamaWeights.LoadFromFile(p);
        Console.WriteLine($"load {sw.Elapsed.TotalSeconds:F1}s");
        var executor = new StatelessExecutor(weights, p);
        foreach (var text in new[]
        {
            "um so i think we should meet on friday at three pm",
            "send the report to john no wait to maria by friday",
        })
        {
            var prompt = $"tone: casual\ntext: {text}\nout:\n";
            sw.Restart();
            var output = new System.Text.StringBuilder();
            await foreach (var t in executor.InferAsync(prompt, new InferenceParams { MaxTokens = 60, AntiPrompts = new[] { "\n" } }))
                output.Append(t);
            Console.WriteLine($"{sw.Elapsed.TotalSeconds:F2}s | {text}\n   -> {output.ToString().Trim()}");
        }
    }
}
