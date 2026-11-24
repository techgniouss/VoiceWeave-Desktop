using System.Net;
using System.Security.Cryptography;
using System.Text;
using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class UpdateCheckerTests : IDisposable
{
    private const string Repo = "techgniouss/VoiceWeave-Desktop";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-upd-" + Guid.NewGuid().ToString("N"));

    public UpdateCheckerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Release(string tag = "v0.2.0", string? installerUrl = null, string? checksumUrl = null, bool draft = false, bool pre = false)
    {
        installerUrl ??= $"https://github.com/{Repo}/releases/download/{tag}/VoiceWeaveSetup.exe";
        checksumUrl ??= $"https://github.com/{Repo}/releases/download/{tag}/VoiceWeaveSetup.exe.sha256";
        return $$"""
        {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{pre.ToString().ToLowerInvariant()}},"body":"New things",
         "assets":[{"name":"VoiceWeaveSetup.exe","browser_download_url":"{{installerUrl}}"},
                   {"name":"VoiceWeaveSetup.exe.sha256","browser_download_url":"{{checksumUrl}}"}]}
        """;
    }

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("0.10.3", 0, 10, 3)]
    [InlineData("v1.2", 1, 2, 0)]
    public void Tags_parse_to_versions(string tag, int major, int minor, int patch)
        => Assert.Equal(new Version(major, minor, patch), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void Odd_tags_are_not_versions(string tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void A_newer_release_is_offered_with_its_files()
    {
        var info = UpdateChecker.ParseRelease(Release(), new Version(0, 1, 0, 0), Repo);
        Assert.NotNull(info);
        Assert.Equal(new Version(0, 2, 0), info!.Version);
        Assert.EndsWith("VoiceWeaveSetup.exe", info.InstallerUrl);
        Assert.EndsWith("VoiceWeaveSetup.exe.sha256", info.ChecksumUrl);
        Assert.Equal("New things", info.Notes);
    }

    [Fact]
    public void The_same_or_an_older_release_is_not_offered()
    {
        Assert.Null(UpdateChecker.ParseRelease(Release(), new Version(0, 2, 0, 0), Repo));
        Assert.Null(UpdateChecker.ParseRelease(Release(), new Version(0, 3, 0, 0), Repo));
    }

    [Fact]
    public void Drafts_and_prereleases_are_ignored()
    {
        Assert.Null(UpdateChecker.ParseRelease(Release(draft: true), new Version(0, 1, 0), Repo));
        Assert.Null(UpdateChecker.ParseRelease(Release(pre: true), new Version(0, 1, 0), Repo));
    }

    [Fact]
    public void A_release_without_a_checksum_is_refused()
    {
        var json = """{"tag_name":"v0.2.0","assets":[{"name":"VoiceWeaveSetup.exe","browser_download_url":"https://github.com/techgniouss/VoiceWeave-Desktop/releases/download/v0.2.0/VoiceWeaveSetup.exe"}]}""";
        Assert.Null(UpdateChecker.ParseRelease(json, new Version(0, 1, 0), Repo));
    }

    [Fact]
    public void Files_from_another_place_are_refused()
    {
        var json = Release(installerUrl: "https://evil.example.com/VoiceWeaveSetup.exe");
        Assert.Null(UpdateChecker.ParseRelease(json, new Version(0, 1, 0), Repo));
        var other = Release(installerUrl: "https://github.com/someone-else/VoiceWeave-Desktop/releases/download/v0.2.0/VoiceWeaveSetup.exe");
        Assert.Null(UpdateChecker.ParseRelease(other, new Version(0, 1, 0), Repo));
    }

    [Fact]
    public void Garbage_json_means_no_update() => Assert.Null(UpdateChecker.ParseRelease("<html>rate limited</html>", new Version(0, 1, 0), Repo));

    [Fact]
    public void Checksum_files_give_the_hash()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, UpdateChecker.ParseChecksum($"{hash.ToUpperInvariant()}  VoiceWeaveSetup.exe\n"));
        Assert.Null(UpdateChecker.ParseChecksum("not a hash"));
    }

    private sealed class Server(Func<string, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(reply(request.RequestUri!.ToString()));
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task Check_reads_the_latest_release()
    {
        var checker = new UpdateChecker(new HttpClient(new Server(url =>
            url.EndsWith("/releases/latest") ? Ok(Release()) : new HttpResponseMessage(HttpStatusCode.NotFound))));
        var info = await checker.CheckAsync(new Version(0, 1, 0), CancellationToken.None);
        Assert.Equal(new Version(0, 2, 0), info?.Version);
    }

    [Fact]
    public async Task Check_returns_nothing_when_github_is_unreachable_or_limited()
    {
        var checker = new UpdateChecker(new HttpClient(new Server(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))));
        Assert.Null(await checker.CheckAsync(new Version(0, 1, 0), CancellationToken.None));
    }

    [Fact]
    public async Task A_download_that_matches_its_checksum_is_kept()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 5000));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var checker = new UpdateChecker(new HttpClient(new Server(url =>
            url.EndsWith(".sha256") ? Ok($"{hash}  VoiceWeaveSetup.exe\n") : Ok(bytes))));
        var info = UpdateChecker.ParseRelease(Release(), new Version(0, 1, 0), Repo)!;

        var progress = new List<double>();
        var path = await checker.DownloadAsync(info, _dir, new Progress<double>(progress.Add), CancellationToken.None);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("VoiceWeaveSetup-0.2.0.exe", Path.GetFileName(path));
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_deleted_and_rejected()
    {
        var checker = new UpdateChecker(new HttpClient(new Server(url =>
            url.EndsWith(".sha256") ? Ok(new string('0', 64) + "  VoiceWeaveSetup.exe") : Ok(new byte[] { 1, 2, 3 }))));
        var info = UpdateChecker.ParseRelease(Release(), new Version(0, 1, 0), Repo)!;

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(info, _dir, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
