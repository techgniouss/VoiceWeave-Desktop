namespace VoiceWeave.Engine;

/// <summary>The model files and recogniser for each supported language, with the hashes pinned.</summary>
public static class LanguagePacks
{
    private const string TranslyBase = "https://github.com/saeedtechshop/transly-speech-packs/releases/download/indic-v1/";
    private const string HindiBase = "https://huggingface.co/OpenVoiceOS/ai4bharat-indicconformer-hi-onnx/resolve/8960b8611af5b8c375d442d52907360176410c8b/";

    // sherpa-onnx refuses a NeMo CTC model without these entries; the Hindi export ships without them.
    private static readonly (string, string)[] HindiMetadata =
    {
        ("vocab_size", "257"), ("normalize_type", "per_feature"), ("subsampling_factor", "4"), ("model_type", "EncDecCTCModelBPE"),
    };

    public static LanguagePack For(Language language) => language switch
    {
        Language.Telugu => Indic(Language.Telugu, "telugu-indic-v1",
            new ModelSpec("telugu-indic-v1/indicconformer-te-int8-matmul.onnx", TranslyBase + "indicconformer-te-int8-matmul.onnx",
                "b99c4ce95c572a50d4f484a88cc7e8b502e2f2ba3d725c5a1937f12ff8536819", 186_479_075),
            new ModelSpec("telugu-indic-v1/indicconformer-te-tokens.txt", TranslyBase + "indicconformer-te-tokens.txt",
                "dc0b7bbb9eca8d3c8aebf1eb86e3144c90d692b4008cf5a32d46437b942b23ac", 2_706)),
        Language.Tamil => Indic(Language.Tamil, "tamil-indic-v1",
            new ModelSpec("tamil-indic-v1/indicconformer-ta-int8-matmul.onnx", TranslyBase + "indicconformer-ta-int8-matmul.onnx",
                "4cc19473ccb7a23b53f65ba3dad9f3c4e53194551f695d080d10c9e39f13ef94", 186_479_075),
            new ModelSpec("tamil-indic-v1/indicconformer-ta-tokens.txt", TranslyBase + "indicconformer-ta-tokens.txt",
                "f5a7955261f7eb7156521eecaf17a89521a507d2b740a3a60a2e6f6fb9b3ad36", 3_042)),
        Language.Hindi => Indic(Language.Hindi, "hindi-indic-v1",
            new ModelSpec("hindi-indic-v1/model.int8.onnx", HindiBase + "model.int8.onnx",
                "c2044177b6d44494fe40be755466845a4c7bd1b5a5d1f6a15b33b2b4ba6011cd", 137_677_313,
                Transform: path => OnnxMetadata.AppendAsync(path, HindiMetadata),
                InstalledSize: 137_677_313 + OnnxMetadata.Encode(HindiMetadata).Length),
            new ModelSpec("hindi-indic-v1/vocab.txt", HindiBase + "vocab.txt",
                "152e58dc9469feb8f74fb006245616ad7cd91b3f7a5ace72ec53f970db10c981", 2_700)),
        _ => new LanguagePack(Language.English, Parakeet.Files.Append(LlamaCleaner.File).ToArray(), dir => new Parakeet(dir)),
    };

    private static LanguagePack Indic(Language language, string folder, ModelSpec model, ModelSpec tokens) =>
        new(language, new[] { model, tokens },
            dir => new IndicConformer(Path.Combine(dir, model.Name), Path.Combine(dir, tokens.Name)));
}
