using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiceWeave.Engine;

/// <summary>A newer release found on GitHub: where to get the installer and the file holding its SHA-256.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string InstallerUrl, string ChecksumUrl, string Notes);

/// <summary>
/// Looks for a newer GitHub Release of the app and downloads its installer, checking the SHA-256 published beside it.
/// Only files that belong to the project's own release downloads are accepted.
/// </summary>
public sealed class UpdateChecker
{
    public const string DefaultRepo = "techgniouss/VoiceWeave-Desktop";
    private const string InstallerName = "VoiceWeaveSetup.exe";

    private readonly HttpClient _http;
    private readonly string _repo;

    public UpdateChecker(HttpClient http, string repo = DefaultRepo)
    {
        _http = http;
        _repo = repo;
    }

    /// <summary>"v0.2.0" or "0.2" to a three-part version; null when the text is not a version.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var m = Regex.Match(tag ?? "", @"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return null;
        int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0;
        return new Version(Part(1), Part(2), Part(3));
    }

    /// <summary>The first 64-hex-digit word of a ".sha256" file (sha256sum format), lower-cased; null if there is none.</summary>
    public static string? ParseChecksum(string text)
    {
        var m = Regex.Match(text ?? "", @"\b([0-9a-fA-F]{64})\b");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>The release described by GitHub's "latest release" JSON, if it is newer than <paramref name="current"/> and complete.</summary>
    public static UpdateInfo? ParseRelease(string json, Version current, string repo = DefaultRepo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Flag(root, "draft") || Flag(root, "prerelease")) return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var latest = ParseVersion(tag);
            if (latest == null || latest <= Normalize(current)) return null;

            string? installer = null, checksum = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name == InstallerName) installer = url;
                    else if (name == InstallerName + ".sha256") checksum = url;
                }
            var prefix = $"https://github.com/{repo}/releases/download/";
            if (installer == null || checksum == null || !installer.StartsWith(prefix, StringComparison.Ordinal) || !checksum.StartsWith(prefix, StringComparison.Ordinal))
                return null;

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new UpdateInfo(latest, tag!, installer, checksum, notes);
        }
        catch (JsonException) { return null; }
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Asks GitHub for the latest release. Null when there is nothing newer, or when GitHub cannot be reached.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repo}/releases/latest");
            request.Headers.UserAgent.ParseAdd("VoiceWeave-updater");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return ParseRelease(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), current, _repo);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    /// <summary>Downloads the installer into <paramref name="folder"/> and checks it against the published SHA-256.
    /// A file that does not match is deleted and <see cref="InvalidDataException"/> is thrown.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var expected = ParseChecksum(await _http.GetStringAsync(info.ChecksumUrl, ct).ConfigureAwait(false))
            ?? throw new InvalidDataException("The release has no usable checksum.");

        var target = Path.Combine(folder, $"VoiceWeaveSetup-{info.Version}.exe");
        var part = target + ".part";
        try
        {
            using var response = await _http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var hash = SHA256.Create();
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = File.Create(part))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (total is > 0) progress?.Report((double)done / total.Value);
                }
            }
            hash.TransformFinalBlock([], 0, 0);
            if (!string.Equals(Convert.ToHexString(hash.Hash!), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded update does not match its checksum.");
            File.Move(part, target, overwrite: true);
            return target;
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { }
            throw;
        }
    }
}
