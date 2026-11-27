using System.Diagnostics;
using System.Threading.Channels;
using SherpaOnnx;

namespace VoiceWeave.Engine;

/// <summary>Something that listens for one phrase in 16 kHz audio (the real model, or a stand-in in tests).</summary>
public interface IWakeSpotter : IDisposable
{
    /// <summary>Forgets the audio heard so far.</summary>
    void Reset();
    /// <returns>True when the phrase was just heard.</returns>
    bool Feed(float[] samples);
    /// <summary>Listens for a different phrase from now on.</summary>
    void SetPhrase(string phrase);
}

/// <summary>A small streaming model (sherpa-onnx keyword spotter) that listens for one phrase in 16 kHz audio.</summary>
public sealed class WakeWordSpotter : IWakeSpotter
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private readonly KeywordSpotter _spotter;
    private readonly WakeWordTokenizer _tokenizer;
    private readonly float _boost, _threshold;
    private OnlineStream _stream;
    private bool _disposed;

    /// <param name="modelDir">Folder holding the files named in <see cref="WakeWordModel.Files"/>.</param>
    /// <param name="boost">How strongly the phrase is favoured over other sounds; higher finds it more easily and also fires more falsely.</param>
    /// <param name="threshold">How sure the model must be (0 to 1); lower fires more easily.</param>
    public WakeWordSpotter(string modelDir, string phrase, float boost = 2.0f, float threshold = 0.25f, int threads = 1)
    {
        _boost = boost;
        _threshold = threshold;
        _tokenizer = WakeWordTokenizer.FromTokensFile(Path.Combine(modelDir, WakeWordModel.Tokens));
        var line = _tokenizer.KeywordLine(phrase, boost, threshold)
            ?? throw new ArgumentException("That phrase can't be spelled with the wake word model. Use plain English words.", nameof(phrase));

        var config = new KeywordSpotterConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDir, WakeWordModel.Encoder);
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDir, WakeWordModel.Decoder);
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDir, WakeWordModel.Joiner);
        config.ModelConfig.Tokens = Path.Combine(modelDir, WakeWordModel.Tokens);
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Debug = 0;
        // The spotter insists on a keywords file when it is created; the phrase can be changed per stream afterwards.
        var keywordsFile = Path.Combine(Path.GetTempPath(), "voiceweave-keywords-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(keywordsFile, line + "\n");
        try
        {
            config.KeywordsFile = keywordsFile;
            _spotter = new KeywordSpotter(config);
        }
        finally { File.Delete(keywordsFile); }
        _stream = _spotter.CreateStream(line);
    }

    /// <summary>Whether <paramref name="phrase"/> can be listened for with this model.</summary>
    public bool CanSpell(string phrase) => _tokenizer.Encode(phrase) != null;

    /// <summary>Listens for a different phrase from now on.</summary>
    public void SetPhrase(string phrase)
    {
        var line = _tokenizer.KeywordLine(phrase, _boost, _threshold)
            ?? throw new ArgumentException("That phrase can't be spelled with the wake word model. Use plain English words.", nameof(phrase));
        lock (_gate)
        {
            if (_disposed) return;
            var old = _stream;
            _stream = _spotter.CreateStream(line);
            old.Dispose();
        }
    }

    /// <summary>Forgets the audio heard so far (after a gap, so a half-heard phrase is not joined to new sound).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _spotter.Reset(_stream);
        }
    }

    /// <returns>True when the phrase was just heard.</returns>
    public bool Feed(float[] samples)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _stream.AcceptWaveform(SampleRate, samples);
            var heard = false;
            while (_spotter.IsReady(_stream))
            {
                _spotter.Decode(_stream);
                if (_spotter.GetResult(_stream).Keyword.Length > 0)
                {
                    heard = true;
                    _spotter.Reset(_stream);
                }
            }
            return heard;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
            _spotter.Dispose();
        }
    }
}

/// <summary>Where the wake word model lives and which files it needs.</summary>
public static class WakeWordModel
{
    public const string Folder = "wakeword-kws-v1";
    public const string Encoder = "encoder.onnx";
    public const string Decoder = "decoder.onnx";
    public const string Joiner = "joiner.onnx";
    public const string Tokens = "tokens.txt";

    // The official k2-fsa release is one archive (17.6 MB); only the small int8 model (about 5 MB) is kept from it.
    private static readonly ModelSpec Archive = new(
        Folder + "/kws-archive.tar.bz2",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/kws-models/sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01.tar.bz2",
        "f170013b4716e41b62b9bfd809687c207cef798ef9bc6534d524e17af9b6561a", 17_626_723);

    private static readonly (string InArchive, string Installed, string Sha256)[] Wanted =
    {
        ("encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx", Encoder, "1e721676515bcd42a186979733981213c66c80db680e1cc582dfedf3be76e678"),
        ("decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx", Decoder, "e40ff43297abe815e8898494c17e71bba2152d9d40fa3eb803f75d0f7533329a"),
        ("joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx", Joiner, "eae9da0c7e1e6c6a3f4cc42d167899c388f6c6701b94cb96320e4f55df79624c"),
        ("tokens.txt", Tokens, "fd2ded4050a55d2b1578870ba8697d02371980217806b7558bd0a5cc60f3ba53"),
    };

    /// <summary>Whether the model files are already installed and intact.</summary>
    public static bool IsInstalled(string modelsDir)
    {
        var dir = Path.Combine(modelsDir, Folder);
        return Wanted.All(w => File.Exists(Path.Combine(dir, w.Installed)) && File.Exists(Path.Combine(dir, w.Installed + ".sha256")) &&
            string.Equals(File.ReadAllText(Path.Combine(dir, w.Installed + ".sha256")).Trim(), w.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Downloads the model (first time only, verified against a pinned hash) and keeps just the files the spotter needs.</summary>
    public static async Task EnsureAsync(ModelManager models, string modelsDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled(modelsDir)) return;
        var archive = await models.EnsureAsync(Archive, modelsDir, progress, ct);
        var dir = Path.Combine(modelsDir, Folder);
        await Task.Run(() => Extract(archive, dir), ct);
        File.Delete(archive);
        var marker = archive + ".sha256";
        if (File.Exists(marker)) File.Delete(marker);
    }

    private static void Extract(string archive, string dir)
    {
        using var file = File.OpenRead(archive);
        using var tar = new ICSharpCode.SharpZipLib.Tar.TarInputStream(new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(file), System.Text.Encoding.UTF8);
        var found = new HashSet<string>();
        for (var entry = tar.GetNextEntry(); entry != null; entry = tar.GetNextEntry())
        {
            if (entry.IsDirectory) continue;
            var want = Wanted.FirstOrDefault(w => w.InArchive == Path.GetFileName(entry.Name));
            if (want.InArchive == null) continue;
            var part = Path.Combine(dir, want.Installed + ".part");
            using (var output = File.Create(part)) tar.CopyEntryContents(output);
            string actual;
            using (var check = File.OpenRead(part)) actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(check)).ToLowerInvariant();
            if (!string.Equals(actual, want.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidDataException($"{want.Installed} in the wake word download is not the expected file; it was discarded.");
            }
            File.Move(part, Path.Combine(dir, want.Installed), overwrite: true);
            File.WriteAllText(Path.Combine(dir, want.Installed + ".sha256"), want.Sha256);
            found.Add(want.Installed);
        }
        if (found.Count != Wanted.Length) throw new InvalidDataException("The wake word download is missing some files.");
    }
}

/// <summary>
/// Listens to the microphone for the wake phrase on its own thread, so the audio callback never waits for the model. Does nothing
/// while <see cref="Paused"/> says so (a dictation is already under way), and measures how much of the CPU it uses.
/// </summary>
public sealed class WakeWordService : IDisposable
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

    private readonly IWakeSpotter _spotter;
    private readonly Channel<float[]> _audio = Channel.CreateBounded<float[]>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _worker;
    private long _busyTicks;
    private long _audioSamples;
    private readonly VoiceWeave.Core.QuietGate _gate = new();
    private readonly VoiceWeave.Core.LevelWindow _levels = new();
    private DateTime _lastHeard = DateTime.MinValue;
    private volatile bool _restart = true;

    /// <summary>True while the phrase should not be listened for (recording or processing a dictation).</summary>
    public Func<bool> Paused { get; set; } = () => false;

    /// <summary>
    /// How loud the phrase must have been (0 to 1, as the loudest 30 ms of the last two seconds) to count. Someone at the microphone
    /// is loud; a TV or people across the room are quiet. 0 accepts any loudness.
    /// </summary>
    public volatile float MinLevel;

    /// <summary>Raised on the worker thread when the phrase is heard.</summary>
    public event Action? Heard;

    /// <summary>Raised on the worker thread when the phrase was heard but too quietly to count, with how loud it was.</summary>
    public event Action<float>? TooQuiet;

    public WakeWordService(IWakeSpotter spotter)
    {
        _spotter = spotter;
        _worker = Task.Run(RunAsync);
    }

    public IWakeSpotter Spotter => _spotter;

    /// <summary>Seconds of CPU the model used per second of audio it listened to (0.02 is two percent of one core).</summary>
    public double CpuPerAudioSecond
    {
        get
        {
            var audio = Interlocked.Read(ref _audioSamples) / (double)WakeWordSpotter.SampleRate;
            var busy = TimeSpan.FromTicks(Interlocked.Read(ref _busyTicks)).TotalSeconds;
            return audio <= 0 ? 0 : busy / audio;
        }
    }

    /// <summary>Called from the microphone callback; cheap, never blocks. Silence is dropped here, so a quiet room costs next to nothing.</summary>
    public void Feed(float[] samples)
    {
        if (Paused()) { _gate.Reset(); _levels.Clear(); return; }
        _levels.Push(samples);
        Interlocked.Add(ref _audioSamples, samples.Length); // all the sound heard, quiet or not, so the load figure is per second of listening
        var passed = _gate.Process(samples, out var restart);
        if (restart) _restart = true;
        foreach (var chunk in passed) _audio.Writer.TryWrite(chunk);
    }

    private async Task RunAsync()
    {
        await foreach (var samples in _audio.Reader.ReadAllAsync())
        {
            try
            {
                if (_restart) { _restart = false; _spotter.Reset(); }
                var timer = Stopwatch.StartNew();
                var heard = _spotter.Feed(samples);
                Interlocked.Add(ref _busyTicks, timer.Elapsed.Ticks);
                if (!heard || DateTime.UtcNow - _lastHeard < Cooldown) continue;
                var level = _levels.Peak;
                if (level < MinLevel) { TooQuiet?.Invoke(level); continue; }
                _lastHeard = DateTime.UtcNow;
                Heard?.Invoke();
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException) { }
        }
    }

    public void Dispose()
    {
        _audio.Writer.TryComplete();
        try { _worker.Wait(1000); } catch (AggregateException) { }
        _spotter.Dispose();
    }
}
