using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace VoiceWeave.Engine;

/// <summary>Spokenly Cleanup 1 (Qwen3-0.6B fine-tune, Q8 GGUF) on the CPU. Loads on first use and unloads when idle.</summary>
public sealed class LlamaCleaner : ICleaner
{
    public static readonly ModelSpec File = new(
        "cleanup-1/cleanup-1-q8_0.gguf",
        "https://huggingface.co/spokenly/cleanup-1-gguf/resolve/main/cleanup-1-q8_0.gguf",
        "66509e0fe1578d2ac4b0773c79a6c83e8d6f218618dfe11e4532beacd999b6e8",
        639_442_784);

    private string _modelPath;
    private readonly int _threads;
    private readonly TimeSpan _idleUnload;
    private readonly object _gate = new();
    private LLamaWeights? _weights;
    private ModelParams? _params;
    private Timer? _idleTimer;

    public LlamaCleaner(string modelDir, int threads = 4, TimeSpan? idleUnload = null)
    {
        _modelPath = Path.Combine(modelDir, File.Name);
        _threads = threads;
        // Loading takes several seconds, so keep the model (about 0.7 GB) ready for a working session.
        _idleUnload = idleUnload ?? TimeSpan.FromHours(3);
    }

    /// <summary>Switches to another copy of the model (the smaller one, or back). The old one is unloaded; the new one loads on the next sentence.</summary>
    public void UseModel(string path)
    {
        lock (_gate)
        {
            if (string.Equals(path, _modelPath, StringComparison.OrdinalIgnoreCase)) return;
            _weights?.Dispose();
            _weights = null;
            _modelPath = path;
        }
    }

    private const int MaxOutputTokens = 140;

    public static string BuildPrompt(string text, Tone tone, IReadOnlyList<string>? vocabulary = null)
    {
        var t = tone switch { Tone.Formal => "formal", Tone.VeryCasual => "very casual", _ => "casual" };
        var terms = vocabulary is { Count: > 0 } ? $"terms: {string.Join(", ", vocabulary)}\n" : "";
        return $"tone: {t}\n{terms}text: {text}\nout:\n";
    }

    public string Clean(string text, Tone tone) => Clean(text, tone, null);

    public string Clean(string text, Tone tone, IReadOnlyList<string>? vocabulary)
    {
        // Text and answer must fit the 512-token window; a stretch this long without a pause is left as recognised (CleanupService falls back).
        if (text.Length > 600) throw new InvalidOperationException("text too long for the cleanup window");
        lock (_gate)
        {
            EnsureLoaded();
            // Cleaned text is about as long as the input, so cap generation near that instead of the 160-token worst case.
            var maxTokens = Math.Min(MaxOutputTokens, text.Length / 3 + 12);
            var executor = new StatelessExecutor(_weights!, _params!);
            var infer = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "\n" },
                SamplingPipeline = new GreedySamplingPipeline(), // same answer every time, and no sampling work
            };
            var sb = new StringBuilder();
            foreach (var piece in executor.InferAsync(BuildPrompt(text, tone, vocabulary), infer).ToBlockingEnumerable())
                sb.Append(piece);
            _idleTimer?.Change(_idleUnload, Timeout.InfiniteTimeSpan);
            return sb.ToString().Trim();
        }
    }

    private void EnsureLoaded()
    {
        if (_weights != null) return;
        // A sentence and its cleaned copy fit easily in 512 tokens; a smaller context is quicker to set up for every call.
        _params = new ModelParams(_modelPath) { ContextSize = 512, GpuLayerCount = 0, Threads = _threads, FlashAttention = true };
        _weights = LLamaWeights.LoadFromFile(_params);
        _idleTimer ??= new Timer(_ => Unload(), null, _idleUnload, Timeout.InfiniteTimeSpan);
    }

    private void Unload()
    {
        lock (_gate)
        {
            _weights?.Dispose();
            _weights = null;
        }
    }

    public void Dispose()
    {
        _idleTimer?.Dispose();
        Unload();
    }
}
