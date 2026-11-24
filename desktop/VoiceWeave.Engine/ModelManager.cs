using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace VoiceWeave.Engine;

/// <summary>A downloadable model file pinned by size and SHA-256.</summary>
public sealed record ModelSpec(string Name, string Url, string Sha256, long Size);

/// <summary>Downloads model files once, resumable, verified against a pinned hash, installed atomically.</summary>
public sealed class ModelManager(HttpClient http)
{
    public async Task<string> EnsureAsync(ModelSpec spec, string dir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var final = Path.GetFullPath(Path.Combine(dir, spec.Name));
        Directory.CreateDirectory(Path.GetDirectoryName(final)!); // names can carry a subfolder, e.g. "parakeet-v2/encoder.int8.onnx"
        var marker = final + ".sha256";
        var part = final + ".part";

        if (File.Exists(final))
        {
            if (new FileInfo(final).Length == spec.Size &&
                (MarkerMatches(marker, spec) || string.Equals(await HashAsync(final, ct), spec.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                await File.WriteAllTextAsync(marker, spec.Sha256, ct);
                progress?.Report(1);
                return final;
            }
            File.Delete(final); // wrong size or contents: fetch again
            if (File.Exists(marker)) File.Delete(marker);
        }

        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have >= spec.Size) { File.Delete(part); have = 0; }

        using var request = new HttpRequestMessage(HttpMethod.Get, spec.Url);
        if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var append = response.StatusCode == HttpStatusCode.PartialContent && have > 0;
        if (!append) have = 0;

        await using (var output = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[1 << 20];
            var total = have;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                total += n;
                progress?.Report(Math.Min(1.0, (double)total / spec.Size));
            }
        }

        var actual = await HashAsync(part, ct);
        if (!string.Equals(actual, spec.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(part);
            throw new InvalidDataException($"{spec.Name} failed its SHA-256 check; the download was discarded.");
        }

        File.Move(part, final, overwrite: true);
        await File.WriteAllTextAsync(marker, spec.Sha256, ct);
        progress?.Report(1);
        return final;
    }

    private static bool MarkerMatches(string marker, ModelSpec spec) =>
        File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), spec.Sha256, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
    }
}
