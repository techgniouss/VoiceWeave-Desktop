namespace VoiceWeave.Engine;

/// <summary>What a language needs: its model files and how to build the recogniser from them.</summary>
public sealed record LanguagePack(Language Language, IReadOnlyList<ModelSpec> Files, Func<string, IAsr> Create);

/// <summary>Downloads (once) and loads the recogniser for a language on demand; concurrent callers share the work.</summary>
public sealed class EngineManager(ModelManager models, string modelDir, Func<Language, LanguagePack> packs) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Language, Task<IAsr>> _loads = new();

    public Task EnsureReadyAsync(Language language, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_loads.TryGetValue(language, out var existing) && !existing.IsFaulted && !existing.IsCanceled) return existing;
            var load = LoadAsync(language, progress, ct);
            _loads[language] = load;
            return load;
        }
    }

    public bool IsReady(Language language)
    {
        lock (_gate) return _loads.TryGetValue(language, out var t) && t.IsCompletedSuccessfully;
    }

    public IAsr Get(Language language)
    {
        lock (_gate)
        {
            if (_loads.TryGetValue(language, out var t) && t.IsCompletedSuccessfully) return t.Result;
        }
        throw new InvalidOperationException($"{Languages.DisplayName(language)} is not loaded yet.");
    }

    private async Task<IAsr> LoadAsync(Language language, IProgress<double>? progress, CancellationToken ct)
    {
        var pack = packs(language);
        var total = Math.Max(1, pack.Files.Sum(f => f.Size));
        long done = 0;
        foreach (var spec in pack.Files)
        {
            var before = done;
            var size = spec.Size;
            var filePart = new DelegateProgress(p => progress?.Report(Math.Min(1.0, (before + p * size) / total)));
            await models.EnsureAsync(spec, modelDir, filePart, ct);
            done += spec.Size;
        }
        progress?.Report(1.0);
        return await Task.Run(() =>
        {
            var engine = pack.Create(modelDir);
            // The first run of a freshly loaded model is several times slower than the rest; do it now on silence.
            engine.Transcribe(new float[16000]);
            return engine;
        }, ct);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var t in _loads.Values)
                if (t.IsCompletedSuccessfully) t.Result.Dispose();
            _loads.Clear();
        }
    }

    private sealed class DelegateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
