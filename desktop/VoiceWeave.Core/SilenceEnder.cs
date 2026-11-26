namespace VoiceWeave.Core;

public enum EndAction { None, Finish, Cancel }

/// <summary>
/// Ends a hands-free dictation that began with the wake word, since there is no key to let go of: after the person has spoken and
/// then been quiet for a while it finishes, and if they never say anything it gives up.
/// </summary>
public sealed class SilenceEnder(float speechLevel = 0.012f, long silenceMs = 2200, long noSpeechMs = 7000)
{
    private long _startedAt;
    private long _lastSpeech;
    private bool _spoke;

    public void Start(long nowMs)
    {
        _startedAt = nowMs;
        _lastSpeech = nowMs;
        _spoke = false;
    }

    /// <param name="rms">Loudness of the latest slice of audio (0 to 1).</param>
    public EndAction OnLevel(float rms, long nowMs)
    {
        if (rms >= speechLevel) { _spoke = true; _lastSpeech = nowMs; return EndAction.None; }
        if (_spoke) return nowMs - _lastSpeech >= silenceMs ? EndAction.Finish : EndAction.None;
        return nowMs - _startedAt >= noSpeechMs ? EndAction.Cancel : EndAction.None;
    }
}
