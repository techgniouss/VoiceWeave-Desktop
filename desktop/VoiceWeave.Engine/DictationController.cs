using System.Threading.Channels;
using VoiceWeave.Core;

namespace VoiceWeave.Engine;

public enum DictationState { Idle, Recording, Processing }

/// <summary>
/// Runs one dictation after another: start recording (holding or hands-free), finish, then recognise, clean and paste in
/// order on a background worker. Recording can begin while an earlier utterance is still being processed, and utterances
/// spoken while the models are still loading wait for them instead of being dropped.
/// </summary>
public sealed class DictationController : IDisposable
{
    private readonly Recorder _recorder;
    private readonly Func<PipelineSession> _newSession;
    private readonly IClipboard _clipboard;
    private readonly IPaster _paster;
    private readonly Func<FocusInfo> _focus;
    private readonly TimeSpan _restoreDelay;
    private readonly Channel<PipelineSession> _jobs = Channel.CreateUnbounded<PipelineSession>();
    private readonly object _gate = new();
    private readonly Task _worker;
    private PipelineSession? _session;
    private bool _recording;
    private bool _latched;
    private int _pending;
    private TaskCompletionSource? _idle;

    public event Action<DictationState>? StateChanged;
    public event Action<string>? Message;
    public event Action<float>? Level;
    /// <summary>Raised when the user asked for the regional language for the dictation in progress.</summary>
    public event Action? RegionalForced;
    /// <summary>Raised after a successful paste with the stage results and the key-release-to-pasted time.</summary>
    public event Action<PipelineResult, TimeSpan>? Pasted;
    /// <summary>Raised for every dictation that produced text, whatever became of it (pasted, or refused for a password field or admin window).</summary>
    public event Action<PipelineResult, InsertOutcome, TimeSpan>? Finished;

    public DictationController(Recorder recorder, Func<PipelineSession> newSession, IClipboard clipboard, IPaster paster,
        Func<FocusInfo> focus, TimeSpan restoreDelay)
    {
        _recorder = recorder;
        _newSession = newSession;
        _clipboard = clipboard;
        _paster = paster;
        _focus = focus;
        _restoreDelay = restoreDelay;
        _recorder.UtteranceSamples += OnUtteranceSamples;
        _worker = Task.Run(WorkAsync);
    }

    public DictationState State
    {
        get { lock (_gate) return Compute(); }
    }

    public bool IsLatched { get { lock (_gate) return _latched; } }

    public void Begin(bool latched)
    {
        lock (_gate)
        {
            if (_recording) return;
            _session = _newSession();           // before the recorder starts, so the pre-roll audio reaches it too
            _recorder.BeginUtterance();
            _recording = true;
            _latched = latched;
        }
        Raise();
    }

    /// <summary>The user asked (Shift held) for the regional language for this dictation.</summary>
    public void ForceRegional()
    {
        PipelineSession? session;
        lock (_gate) session = _recording ? _session : null;
        if (session == null) return;
        if (!session.ForceRegional()) return; // already forced: the key is only repeating
        RegionalForced?.Invoke();
    }

    /// <summary>Turns a held recording into a hands-free one (the key can be released).</summary>
    public void Latch()
    {
        lock (_gate) { if (_recording) _latched = true; }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (!_recording) return;
            _recorder.Cancel();
            _session?.Cancel();
            _session = null;
            _recording = false;
            _latched = false;
        }
        Raise();
    }

    public void Finish()
    {
        PipelineSession session;
        lock (_gate)
        {
            if (!_recording) return;
            _recorder.EndUtterance();           // the session already has the audio; this just stops collecting
            session = _session!;
            _session = null;
            _recording = false;
            _latched = false;
            _pending++;
        }
        _jobs.Writer.TryWrite(session);
        Raise();
    }

    /// <summary>Feeds microphone samples for the live level meter; only reports while recording.</summary>
    public void PushLevel(float[] samples)
    {
        bool recording;
        lock (_gate) recording = _recording;
        if (!recording || samples.Length == 0) return;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        Level?.Invoke((float)Math.Sqrt(sum / samples.Length));
    }

    public Task WaitUntilIdleAsync()
    {
        lock (_gate)
        {
            if (_pending == 0 && !_recording) return Task.CompletedTask;
            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }

    private void OnUtteranceSamples(float[] samples) => _session?.Feed(samples);

    private async Task WorkAsync()
    {
        await foreach (var session in _jobs.Reader.ReadAllAsync())
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Most of the work was done while the user was speaking; this waits for the last sentence.
                var result = await Task.Run(session.Finish);
                if (result.Final.Length == 0)
                {
                    Message?.Invoke("Didn't catch that");
                }
                else
                {
                    var focus = await Task.Run(_focus); // the window we are about to paste into, not the one at key release
                    var outcome = await TextInserter.InsertAsync(_clipboard, _paster, focus, result.Final, _restoreDelay);
                    Finished?.Invoke(result, outcome, started.Elapsed);
                    switch (outcome)
                    {
                        case InsertOutcome.Pasted: Pasted?.Invoke(result, started.Elapsed); break;
                        case InsertOutcome.SkippedPasswordField: Message?.Invoke("Not typing into a password field"); break;
                        case InsertOutcome.SkippedElevatedTarget: Message?.Invoke("Can't type into an admin window"); break;
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Message?.Invoke("Something went wrong: " + e.Message);
            }
            finally
            {
                lock (_gate) _pending--;
                Raise();
            }
        }
    }

    private DictationState Compute() =>
        _recording ? DictationState.Recording : _pending > 0 ? DictationState.Processing : DictationState.Idle;

    private void Raise()
    {
        DictationState state;
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            state = Compute();
            if (state == DictationState.Idle) { idle = _idle; _idle = null; }
        }
        StateChanged?.Invoke(state);
        idle?.TrySetResult();
    }

    public void Dispose()
    {
        _recorder.UtteranceSamples -= OnUtteranceSamples;
        _jobs.Writer.TryComplete();
        try { _worker.Wait(1000); } catch (AggregateException) { }
    }
}
