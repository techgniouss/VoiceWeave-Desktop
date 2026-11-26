using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace VoiceWeave.Engine;

/// <summary>A downloadable model file pinned by size and SHA-256.</summary>
/// <param name="Transform">Optional step run on the verified download before it is installed (for example adding metadata).</param>
/// <param name="InstalledSize">Size of the file after <paramref name="Transform"/>; defaults to <paramref name="Size"/>.</param>
/// <param name="FallbackUrls">Other places with exactly the same file, tried in order if <paramref name="Url"/> fails or serves the wrong bytes.</param>
public sealed record ModelSpec(string Name, string Url, string Sha256, long Size,
    Func<string, Task>? Transform = null, long? InstalledSize = null, IReadOnlyList<string>? FallbackUrls = null);

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
            // A transformed file no longer matches the pinned hash, so only the marker written at install time can vouch for it.
            if (new FileInfo(final).Length == (spec.InstalledSize ?? spec.Size) &&
                (MarkerMatches(marker, spec) ||
                 (spec.Transform == null && string.Equals(await HashAsync(final, ct), spec.Sha256, StringComparison.OrdinalIgnoreCase))))
            {
                await File.WriteAllTextAsync(marker, spec.Sha256, ct);
                return final; // already installed: no progress to report
            }
            File.Delete(final); // wrong size or contents: fetch again
            if (File.Exists(marker)) File.Delete(marker);
        }

        // The main address first, then each other copy. A failure or a file with the wrong hash moves on to the next address;
        // another server's bytes are never appended to a half-finished download, so every later try starts from nothing.
        var urls = new List<string> { spec.Url };
        if (spec.FallbackUrls != null) urls.AddRange(spec.FallbackUrls);
        for (var i = 0; i < urls.Count; i++)
        {
            try
            {
                if (i > 0 && File.Exists(part)) File.Delete(part);
                await FetchAsync(urls[i], spec, part, progress, ct);
                break;
            }
            catch (Exception e) when (i < urls.Count - 1 && e is HttpRequestException or InvalidDataException or IOException)
            {
                // try the next address
            }
        }

        if (spec.Transform != null)
        {
            await spec.Transform(part);
            if (new FileInfo(part).Length != (spec.InstalledSize ?? spec.Size))
            {
                File.Delete(part);
                throw new InvalidDataException($"{spec.Name} has an unexpected size after processing; it was discarded.");
            }
        }

        File.Move(part, final, overwrite: true);
        await File.WriteAllTextAsync(marker, spec.Sha256, ct);
        progress?.Report(1);
        return final;
    }

    /// <summary>Downloads one address into <paramref name="part"/> (resuming it if it is half done) and checks the hash.</summary>
    private async Task FetchAsync(string url, ModelSpec spec, string part, IProgress<double>? progress, CancellationToken ct)
    {
        var have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have >= spec.Size) { File.Delete(part); have = 0; }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
    }

    private static bool MarkerMatches(string marker, ModelSpec spec) =>
        File.Exists(marker) && string.Equals(File.ReadAllText(marker).Trim(), spec.Sha256, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var s = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
    }
}
