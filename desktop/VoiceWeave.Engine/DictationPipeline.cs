using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using VoiceWeave.Core;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.Engine;

/// <param name="Rules">The dictionary rules that changed the text, so a later fix of their output can be traced back to them.</param>
public sealed record PipelineResult(string Raw, string Corrected, string Final, TimeSpan AsrTime, TimeSpan CleanupTime,
    IReadOnlyList<AppliedRule>? Rules = null);

/// <summary>
/// Audio in, clean text out. Speech is cut at its pauses and each stretch is recognised, corrected by the dictionary and
/// cleaned up while the person is still talking, so a long dialogue costs little more waiting at the end than a short one.
/// </summary>
public sealed class DictationPipeline(SpeechRouter router, CleanupService cleanup, CoreDictionary dictionary)
{
    public const int SampleRate = 16000;
    private const double MinSeconds = 0.2;
    private const float SilencePeak = 0.01f;

    /// <summary>Replaceable at runtime so edits to the dictionary file apply without a restart.</summary>
    public CoreDictionary Dictionary { get; set; } = dictionary;

    /// <summary>Write Telugu, Hindi and Tamil in English letters instead of their own script.</summary>
    public Func<bool> RomanOutput { get; set; } = () => false;

    public SpeechRouter Router => router;

    // One recognition at a time: the models use all the CPU, and utterances must come out in order.
    internal readonly object EngineGate = new();

    /// <summary>Starts a dictation. Feed it audio as it is recorded, then call <see cref="PipelineSession.Finish"/>.</summary>
    /// <param name="ready">Completes when the models are loaded; recognition waits for it.</param>
    public PipelineSession StartSession(Tone tone, Func<Task>? ready = null) => new(this, tone, ready ?? (() => Task.CompletedTask));

    /// <summary>Recognises a finished recording in one go.</summary>
    public PipelineResult Run(float[] audio, Tone tone)
    {
        var session = StartSession(tone);
        session.Feed(audio);
        return session.Finish();
    }

    internal Piece? Process(float[] audio, Tone tone, Language? previous, bool forceRegional = false)
    {
        if (audio.Length < MinSeconds * SampleRate || Peak(audio) < SilencePeak) return null;
        lock (EngineGate)
        {
            var sw = Stopwatch.StartNew();
            var segment = router.RecognizeSegment(audio, previous, forceRegional);
            var asrTime = sw.Elapsed;
            if (segment == null) return null;

            var fired = new List<AppliedRule>();
            var corrected = Dictionary.Apply(segment.Text, fired);
            sw.Restart();
            // The cleanup model only understands English; regional text keeps what the recogniser wrote, plus the dictionary.
            var cleaned = Languages.UsesCleanup(segment.Language) ? cleanup.Run(corrected, tone, Dictionary.Terms) : corrected;
            var cleanupTime = sw.Elapsed;
            router.Trace?.Invoke($"   cleanup {cleanupTime.TotalSeconds:F2}s ({segment.Language})");
            // Rules learned from the user's corrections of typed (romanised) text are in English letters, so apply the dictionary again.
            var final = segment.Language != Language.English && RomanOutput() ? Dictionary.Apply(Romanizer.Convert(cleaned), fired) : cleaned;
            return new Piece(segment.Language, segment.Text, corrected, final.Trim(), asrTime, cleanupTime, fired);
        }
    }

    private static float Peak(float[] a)
    {
        var peak = 0f;
        foreach (var s in a) { var v = Math.Abs(s); if (v > peak) peak = v; }
        return peak;
    }

    internal sealed record Piece(Language Language, string Raw, string Corrected, string Final, TimeSpan AsrTime, TimeSpan CleanupTime,
        IReadOnlyList<AppliedRule> Rules);
}

/// <summary>One dictation in progress: audio goes in as it is spoken, finished sentences are processed in the background.</summary>
public sealed class PipelineSession
{
    private readonly DictationPipeline _owner;
    private readonly Tone _tone;
    private readonly Func<Task> _ready;
    private readonly LiveSegmenter _segmenter = new(DictationPipeline.SampleRate);
    private readonly Channel<float[]> _segments = Channel.CreateUnbounded<float[]>();
    private readonly List<DictationPipeline.Piece> _pieces = new();
    private readonly Task _worker;
    private readonly object _gate = new();
    private Exception? _error;
    private volatile bool _cancelled;
    private volatile bool _forceRegional;
    private bool _finished;

    internal PipelineSession(DictationPipeline owner, Tone tone, Func<Task> ready)
    {
        _owner = owner;
        _tone = tone;
        _ready = ready;
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>Adds audio (16 kHz mono). Cheap enough to call from the audio thread.</summary>
    public void Feed(float[] samples)
    {
        lock (_gate)
        {
            if (_finished || _cancelled) return;
            foreach (var segment in _segmenter.Feed(samples)) _segments.Writer.TryWrite(segment);
        }
    }

    /// <summary>Processes the last words, waits for everything and returns the finished text. Rethrows a recognition failure.</summary>
    public PipelineResult Finish()
    {
        lock (_gate)
        {
            if (!_finished && !_cancelled)
            {
                foreach (var tail in _segmenter.Flush()) _segments.Writer.TryWrite(tail);
            }
            _finished = true;
            _segments.Writer.TryComplete();
        }
        _worker.GetAwaiter().GetResult();
        if (_error != null) ExceptionDispatchInfo.Capture(_error).Throw();

        var pieces = _pieces.Where(p => p.Final.Length > 0).ToList();
        return new PipelineResult(
            string.Join(' ', pieces.Select(p => p.Raw)),
            string.Join(' ', pieces.Select(p => p.Corrected)),
            JoinSentences(pieces.Select(p => p.Final)),
            TimeSpan.FromTicks(pieces.Sum(p => p.AsrTime.Ticks)),
            TimeSpan.FromTicks(pieces.Sum(p => p.CleanupTime.Ticks)),
            pieces.SelectMany(p => p.Rules).DistinctBy(r => r.Line).ToList());
    }

    /// <summary>
    /// The user said this dictation is in the regional language: sentences recognised from now on skip the English guess.
    /// (Has no effect in the single-language modes.)
    /// </summary>
    /// <returns>True the first time it is called for this dictation (holding Shift repeats the key).</returns>
    public bool ForceRegional()
    {
        var first = !_forceRegional;
        _forceRegional = true;
        return first;
    }

    /// <summary>Abandons the dictation: nothing more is recognised.</summary>
    public void Cancel()
    {
        _cancelled = true;
        lock (_gate) _segments.Writer.TryComplete();
    }

    private async Task WorkAsync()
    {
        Language? previous = null;
        await foreach (var audio in _segments.Reader.ReadAllAsync())
        {
            if (_cancelled) break;
            try
            {
                await _ready();
                var piece = _owner.Process(audio, _tone, previous, _forceRegional);
                if (piece == null) continue;
                _pieces.Add(piece);
                previous = piece.Language;
            }
            catch (Exception e)
            {
                _error ??= e;
            }
        }
    }

    /// <summary>Joins sentences with a space, starting a sentence with a capital after a full stop, question mark or exclamation mark.</summary>
    internal static string JoinSentences(IEnumerable<string> sentences)
    {
        var result = new System.Text.StringBuilder();
        foreach (var raw in sentences)
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            if (result.Length > 0)
            {
                var last = result[^1];
                if (".?!".IndexOf(last) >= 0 && char.IsLower(s[0])) s = char.ToUpperInvariant(s[0]) + s[1..];
                result.Append(' ');
            }
            result.Append(s);
        }
        return result.ToString();
    }
}
