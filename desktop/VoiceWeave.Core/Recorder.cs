namespace VoiceWeave.Core;

/// <summary>A microphone producing 16 kHz mono float samples.</summary>
public interface IAudioSource : IDisposable
{
    event Action<float[]> Samples;
    void Start();
    void Stop();
}

/// <summary>
/// Keeps the last moments of audio in a pre-roll ring while armed, so an utterance starts with the audio
/// from just before the key press; then collects the utterance until it ends.
/// </summary>
public sealed class Recorder(IAudioSource source, PreRoll preRoll)
{
    private readonly object _gate = new();
    private readonly List<float> _utterance = new();
    private bool _armed;
    private bool _recording;

    /// <summary>Raised with the audio of the current utterance as it arrives (first the pre-roll, then each new piece).</summary>
    public event Action<float[]>? UtteranceSamples;

    public void Arm()
    {
        lock (_gate)
        {
            if (_armed) return;
            _armed = true;
        }
        source.Samples += OnSamples;
        source.Start();
    }

    public void Disarm()
    {
        source.Samples -= OnSamples;
        source.Stop();
        lock (_gate)
        {
            _armed = false;
            _recording = false;
            _utterance.Clear();
            preRoll.Take();
        }
    }

    public void BeginUtterance()
    {
        float[] lead;
        lock (_gate)
        {
            _utterance.Clear();
            lead = preRoll.Take();
            _utterance.AddRange(lead);
            _recording = true;
        }
        if (lead.Length > 0) UtteranceSamples?.Invoke(lead);
    }

    public float[] EndUtterance()
    {
        lock (_gate)
        {
            _recording = false;
            var result = _utterance.ToArray();
            _utterance.Clear();
            return result;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _recording = false;
            _utterance.Clear();
        }
    }

    /// <summary>Samples collected so far in the current utterance (for the live waveform).</summary>
    public int UtteranceLength { get { lock (_gate) return _utterance.Count; } }

    private void OnSamples(float[] samples)
    {
        bool recording;
        lock (_gate)
        {
            if (!_armed) return;
            recording = _recording;
            if (recording) _utterance.AddRange(samples);
            else preRoll.Push(samples);
        }
        if (recording) UtteranceSamples?.Invoke(samples);
    }
}
