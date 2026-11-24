using System.Net;
using System.Security.Cryptography;
using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class LanguageIdManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-lid-" + Guid.NewGuid().ToString("N"));
    public LanguageIdManagerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Server(byte[] body) : HttpMessageHandler
    {
        public int Requests;
        public bool FailNext;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if (FailNext) { FailNext = false; throw new HttpRequestException("offline"); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private sealed class FakeLid : ILanguageIdentifier
    {
        public string Identify(float[] pcm16k) => "en";
    }

    private (LanguageIdManager manager, Server server) Rig()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var sha = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var server = new Server(body);
        var files = new[] { new ModelSpec("language-id/m.bin", "http://h/m", sha, body.Length) };
        return (new LanguageIdManager(new ModelManager(new HttpClient(server)), _dir, files, _ => new FakeLid()), server);
    }

    [Fact]
    public async Task Current_IsNullUntilLoaded_ThenAvailable()
    {
        var (m, _) = Rig();
        Assert.Null(m.Current);
        await m.EnsureReadyAsync();
        Assert.NotNull(m.Current);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneDownload()
    {
        var (m, server) = Rig();
        await Task.WhenAll(m.EnsureReadyAsync(), m.EnsureReadyAsync());
        Assert.Equal(1, server.Requests);
    }

    [Fact]
    public async Task AfterAFailure_TheNextCallRetries()
    {
        var (m, server) = Rig();
        server.FailNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => m.EnsureReadyAsync());
        Assert.Null(m.Current);
        await m.EnsureReadyAsync();
        Assert.NotNull(m.Current);
    }

    [Fact]
    public void ThePinnedModelFilesAreDeclared()
    {
        Assert.Equal(3, WhisperLanguageId.Files.Length);
        Assert.All(WhisperLanguageId.Files, f => Assert.Equal(64, f.Sha256.Length));
    }
}
