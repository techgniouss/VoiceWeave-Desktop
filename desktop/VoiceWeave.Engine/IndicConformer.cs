using SherpaOnnx;

namespace VoiceWeave.Engine;

/// <summary>AI4Bharat IndicConformer (int8, CTC) through sherpa-onnx, for one language. Writes no punctuation.</summary>
public sealed class IndicConformer : IAsr
{
    public const int SampleRate = 16000;
    public const int MaxChunkSeconds = 30;

    private readonly OfflineRecognizer _recognizer;

    public IndicConformer(string modelPath, string tokensPath, int threads = 4)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.NeMoCtc.Model = modelPath;
        config.ModelConfig.Tokens = tokensPath;
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Debug = 0;
        _recognizer = new OfflineRecognizer(config);
    }

    public string Transcribe(float[] pcm16k)
    {
        if (pcm16k.Length == 0) return "";
        var parts = new List<string>();
        foreach (var chunk in AudioChunker.Split(pcm16k, SampleRate, MaxChunkSeconds))
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(SampleRate, chunk);
            _recognizer.Decode(stream);
            var text = stream.Result.Text.Trim();
            if (text.Length > 0) parts.Add(text);
        }
        return string.Join(' ', parts);
    }

    public void Dispose() => _recognizer.Dispose();
}
