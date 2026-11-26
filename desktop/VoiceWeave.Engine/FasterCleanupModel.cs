using LLama;
using LLama.Native;

namespace VoiceWeave.Engine;

/// <summary>
/// An optional smaller copy of the cleanup model (4-bit instead of 8-bit): about 30% quicker and 230 MB lighter, with slightly
/// different answers. It is made once, on this computer, from the model that is already downloaded, so nothing more is fetched.
/// </summary>
public static class FasterCleanupModel
{
    public const string FileName = "cleanup-1/cleanup-1-q4_k_m.gguf";
    private const long MinPlausibleBytes = 200L * 1024 * 1024;

    public static string Path(string modelsDir) => System.IO.Path.Combine(modelsDir, FileName);

    private static string Marker(string modelsDir) => Path(modelsDir) + ".from";

    /// <summary>Whether the smaller copy exists and was made from the model file the app currently pins.</summary>
    public static bool IsReady(string modelsDir)
    {
        try
        {
            var file = Path(modelsDir);
            return File.Exists(file) && new FileInfo(file).Length >= MinPlausibleBytes && File.Exists(Marker(modelsDir)) &&
                   string.Equals(File.ReadAllText(Marker(modelsDir)).Trim(), LlamaCleaner.File.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
    }

    /// <summary>Makes the smaller copy if it is missing or out of date. Takes about half a minute to a minute; run it off the UI thread.</summary>
    public static async Task EnsureAsync(string modelsDir, CancellationToken ct = default)
    {
        if (IsReady(modelsDir)) return;
        var source = System.IO.Path.Combine(modelsDir, LlamaCleaner.File.Name);
        if (!File.Exists(source)) throw new FileNotFoundException("The cleanup model has not been downloaded yet.", source);
        await Task.Run(() => Quantize(source, modelsDir), ct);
    }

    private static void Quantize(string source, string modelsDir)
    {
        NativeApi.llama_empty_call(); // makes sure the llama library is loaded before it is used
        var target = Path(modelsDir);
        var part = target + ".part";
        if (File.Exists(part)) File.Delete(part);
        try
        {
            if (!LLamaQuantizer.Quantize(source, part, LLamaFtype.MOSTLY_Q4_K_M, allowRequantize: true))
                throw new InvalidOperationException("The smaller cleanup model could not be made.");
            if (new FileInfo(part).Length < MinPlausibleBytes) throw new InvalidDataException("The smaller cleanup model came out too small; it was discarded.");
            if (File.Exists(Marker(modelsDir))) File.Delete(Marker(modelsDir));
            File.Move(part, target, overwrite: true);
            File.WriteAllText(Marker(modelsDir), LlamaCleaner.File.Sha256);
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }
}
