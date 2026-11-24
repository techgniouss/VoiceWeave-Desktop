using SherpaOnnx;
using VoiceWeave.Core;

namespace VoiceWeave.Engine;

public interface IAsr : IDisposable
{
    string Transcribe(float[] pcm16k);
}

/// <summary>NVIDIA Parakeet TDT 0.6B v2 (int8) through sherpa-onnx. English, with punctuation and capitalisation.</summary>
public sealed class Parakeet : IAsr
{
    public const int SampleRate = 16000;
    public const int MaxChunkSeconds = 30;

    public static readonly ModelSpec[] Files =
    {
        new("parakeet-v2/encoder.int8.onnx", Url("encoder.int8.onnx"), "92a10bd9c23936ea0566c29f963bc57c2a5df4261b6a8921c39abea66557ca8b", 652_282_309),
        new("parakeet-v2/decoder.int8.onnx", Url("decoder.int8.onnx"), "4d097bae3bac5c96018d5293ba39f1b657296102b142affda40378f38b020f41", 7_257_753),
        new("parakeet-v2/joiner.int8.onnx", Url("joiner.int8.onnx"), "77a29f3e5ac7d37074e0af465391b80f314015d5b97f0f90c6c7767f1d56f428", 1_739_080),
        new("parakeet-v2/tokens.txt", Url("tokens.txt"), "ec182b70dd42113aff6c5372c75cac58c952443eb22322f57bbd7f53977d497d", 9_384),
    };

    private static string Url(string file) =>
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8/resolve/main/" + file;

    private readonly OfflineRecognizer _recognizer;

    /// <param name="modelDir">Directory containing the files from <see cref="Files"/> under <c>parakeet-v2/</c>.</param>
    public Parakeet(string modelDir, int threads = 4)
    {
        var dir = Path.Combine(modelDir, "parakeet-v2");
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 128;
        config.ModelConfig.Transducer.Encoder = Path.Combine(dir, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(dir, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(dir, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(dir, "tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.ModelType = "nemo_transducer";
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
