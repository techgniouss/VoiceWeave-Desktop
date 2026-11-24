using System.Text;
using LLama;
using LLama.Common;

namespace VoiceWeave.Engine;

/// <summary>Spokenly Cleanup 1 (Qwen3-0.6B fine-tune, Q8 GGUF) on the CPU. Loads on first use and unloads when idle.</summary>
public sealed class LlamaCleaner : ICleaner
{
    public static readonly ModelSpec File = new(
        "cleanup-1/cleanup-1-q8_0.gguf",
        "https://huggingface.co/spokenly/cleanup-1-gguf/resolve/main/cleanup-1-q8_0.gguf",
        "66509e0fe1578d2ac4b0773c79a6c83e8d6f218618dfe11e4532beacd999b6e8",
        639_442_784);

    private readonly string _modelPath;
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
        _idleUnload = idleUnload ?? TimeSpan.FromMinutes(5);
    }

    public static string BuildPrompt(string text, Tone tone)
    {
        var t = tone switch { Tone.Formal => "formal", Tone.VeryCasual => "very casual", _ => "casual" };
        return $"tone: {t}\ntext: {text}\nout:\n";
    }

    public string Clean(string text, Tone tone)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var executor = new StatelessExecutor(_weights!, _params!);
            var infer = new InferenceParams { MaxTokens = 160, AntiPrompts = new[] { "\n" } };
            var sb = new StringBuilder();
            foreach (var piece in executor.InferAsync(BuildPrompt(text, tone), infer).ToBlockingEnumerable())
                sb.Append(piece);
            _idleTimer?.Change(_idleUnload, Timeout.InfiniteTimeSpan);
            return sb.ToString().Trim();
        }
    }

    private void EnsureLoaded()
    {
        if (_weights != null) return;
        _params = new ModelParams(_modelPath) { ContextSize = 1024, GpuLayerCount = 0, Threads = _threads };
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
