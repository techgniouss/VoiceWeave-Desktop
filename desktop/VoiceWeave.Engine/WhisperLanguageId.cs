using SherpaOnnx;

namespace VoiceWeave.Engine;

/// <summary>Spoken-language identification with Whisper-base (int8) through sherpa-onnx: about 0.4 s for a few seconds of speech.</summary>
public sealed class WhisperLanguageId : ILanguageIdentifier, IDisposable
{
    public static readonly ModelSpec[] Files =
    {
        new("language-id/base-encoder.int8.onnx", Url("base-encoder.int8.onnx"), "0b8fb1304b6109976038efff5ace81720e00386f3ff6b54ee8c75291ca0a1e11", 29_120_534),
        new("language-id/base-decoder.int8.onnx", Url("base-decoder.int8.onnx"), "9759d217388a01b3a4c7c15533201067b48ae819c4daafc8624e64b9409dc02d", 130_672_026),
        new("language-id/base-tokens.txt", Url("base-tokens.txt"), "b34b360dbb493e781e479794586d661700670d65564001f23024971d1f2fa126", 816_730),
    };

    private static string Url(string file) => "https://huggingface.co/csukuangfj/sherpa-onnx-whisper-base/resolve/main/" + file;

    private readonly SpokenLanguageIdentification _slid;

    public WhisperLanguageId(string modelDir, int threads = 4)
    {
        var dir = Path.Combine(modelDir, "language-id");
        var config = new SpokenLanguageIdentificationConfig();
        config.Whisper.Encoder = Path.Combine(dir, "base-encoder.int8.onnx");
        config.Whisper.Decoder = Path.Combine(dir, "base-decoder.int8.onnx");
        config.NumThreads = threads;
        config.Debug = 0;
        _slid = new SpokenLanguageIdentification(config);
    }

    public string Identify(float[] pcm16k)
    {
        // Whisper looks at the first 30 seconds only, which is plenty for one stretch of speech.
        using var stream = _slid.CreateStream();
        stream.AcceptWaveform(16000, pcm16k);
        return _slid.Compute(stream).Lang;
    }

    public void Dispose() => _slid.Dispose();
}

/// <summary>Downloads (once) and loads the language-ID model; auto modes fall back to English until it is ready.</summary>
public sealed class LanguageIdManager(ModelManager models, string modelDir, IReadOnlyList<ModelSpec> files,
    Func<string, ILanguageIdentifier> create) : IDisposable
{
    private readonly object _gate = new();
    private Task<ILanguageIdentifier>? _load;

    public Task EnsureReadyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_load is { IsFaulted: false, IsCanceled: false }) return _load;
            return _load = LoadAsync(progress, ct);
        }
    }

    /// <summary>The identifier once it is loaded, otherwise null.</summary>
    public ILanguageIdentifier? Current
    {
        get { lock (_gate) return _load is { IsCompletedSuccessfully: true } ? _load.Result : null; }
    }

    private async Task<ILanguageIdentifier> LoadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var total = Math.Max(1, files.Sum(f => f.Size));
        long done = 0;
        foreach (var spec in files)
        {
            var before = done;
            var size = spec.Size;
            await models.EnsureAsync(spec, modelDir, new Relay(p => progress?.Report(Math.Min(1.0, (before + p * size) / total))), ct);
            done += spec.Size;
        }
        progress?.Report(1.0);
        return await Task.Run(() =>
        {
            var identifier = create(modelDir);
            identifier.Identify(new float[16000]); // warm-up: the first run is slower than the rest
            return identifier;
        }, ct);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_load is { IsCompletedSuccessfully: true } && _load.Result is IDisposable d) d.Dispose();
            _load = null;
        }
    }

    private sealed class Relay(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
