using NAudio.Wave;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>Default microphone as 16 kHz mono float via the Windows multimedia API (which converts formats for us).</summary>
public sealed class WaveInMic : IAudioSource
{
    private WaveInEvent? _wave;

    public event Action<float[]>? Samples;

    /// <summary>Device index; -1 is the Windows default recording device.</summary>
    public int DeviceNumber { get; init; } = -1;

    public static IReadOnlyList<string> DeviceNames()
    {
        var names = new List<string>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++) names.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return names;
    }

    public void Start()
    {
        if (_wave != null) return;
        _wave = new WaveInEvent
        {
            DeviceNumber = DeviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 30,
            NumberOfBuffers = 4,
        };
        _wave.DataAvailable += OnData;
        _wave.StartRecording();
    }

    public void Stop()
    {
        var w = _wave;
        _wave = null;
        if (w == null) return;
        w.DataAvailable -= OnData;
        try { w.StopRecording(); } catch (InvalidOperationException) { }
        w.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var n = e.BytesRecorded / 2;
        if (n == 0) return;
        var f = new float[n];
        for (var i = 0; i < n; i++) f[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        Samples?.Invoke(f);
    }

    public void Dispose() => Stop();
}
