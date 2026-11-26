using System.Net;
using System.Security.Cryptography;
using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class ModelManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-models-" + Guid.NewGuid().ToString("N"));

    public ModelManagerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private sealed class FakeServer(byte[] body) : HttpMessageHandler
    {
        public int Requests;
        public long? LastRangeFrom;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            LastRangeFrom = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            if (LastRangeFrom is long from && from > 0)
            {
                var part = body[(int)from..];
                var r = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(part) };
                return Task.FromResult(r);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task Downloads_Verifies_AndInstallsAtomically()
    {
        var body = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        var server = new FakeServer(body);
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://x/m.bin", Sha(body), body.Length), _dir);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task WrongHash_Throws_AndLeavesNoFinalFile()
    {
        var body = new byte[] { 1, 2, 3 };
        var mm = new ModelManager(new HttpClient(new FakeServer(body)));
        var spec = new ModelSpec("m.bin", "http://x/m.bin", new string('0', 64), body.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => mm.EnsureAsync(spec, _dir));
        Assert.False(File.Exists(Path.Combine(_dir, "m.bin")));
        Assert.False(File.Exists(Path.Combine(_dir, "m.bin.part")));
    }

    [Fact]
    public async Task AlreadyValid_SkipsDownload()
    {
        var body = new byte[] { 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(Path.Combine(_dir, "m.bin"), body);
        var server = new FakeServer(body);
        var mm = new ModelManager(new HttpClient(server));
        await mm.EnsureAsync(new ModelSpec("m.bin", "http://x/m.bin", Sha(body), body.Length), _dir);
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task PartialFile_ResumesFromItsLength()
    {
        var body = Enumerable.Range(0, 4000).Select(i => (byte)(i * 7)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_dir, "m.bin.part"), body[..1500]);
        var server = new FakeServer(body);
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://x/m.bin", Sha(body), body.Length), _dir);
        Assert.Equal(1500, server.LastRangeFrom);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CorruptExistingFile_IsReplaced()
    {
        var body = new byte[] { 9, 9, 9, 9 };
        await File.WriteAllBytesAsync(Path.Combine(_dir, "m.bin"), new byte[] { 0, 0 });
        var mm = new ModelManager(new HttpClient(new FakeServer(body)));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://x/m.bin", Sha(body), body.Length), _dir);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }

    // Serves a different answer per address: a body, "gone" (404) or "boom" (connection failure).
    private sealed class MultiServer(Dictionary<string, byte[]?> answers) : HttpMessageHandler
    {
        public readonly List<string> Asked = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Asked.Add(url);
            if (!answers.TryGetValue(url, out var body)) throw new HttpRequestException("connection failed");
            if (body == null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task MainAddressGone_UsesTheFallback()
    {
        var body = Enumerable.Range(0, 3000).Select(i => (byte)(i * 3)).ToArray();
        var server = new MultiServer(new() { ["http://main/m"] = null, ["http://copy/m"] = body });
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://main/m", Sha(body), body.Length, FallbackUrls: new[] { "http://copy/m" }), _dir);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { "http://main/m", "http://copy/m" }, server.Asked);
    }

    [Fact]
    public async Task MainAddressUnreachable_UsesTheFallback()
    {
        var body = new byte[] { 4, 5, 6, 7 };
        var server = new MultiServer(new() { ["http://copy/m"] = body });
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://main/m", Sha(body), body.Length, FallbackUrls: new[] { "http://copy/m" }), _dir);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task MainAddressServesTheWrongBytes_UsesTheFallback()
    {
        var good = new byte[] { 1, 1, 2, 3 };
        var server = new MultiServer(new() { ["http://main/m"] = new byte[] { 9, 9, 9, 9 }, ["http://copy/m"] = good });
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://main/m", Sha(good), good.Length, FallbackUrls: new[] { "http://copy/m" }), _dir);
        Assert.Equal(good, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task MainAddressWorks_FallbackIsNeverAsked()
    {
        var body = new byte[] { 8, 7, 6 };
        var server = new MultiServer(new() { ["http://main/m"] = body, ["http://copy/m"] = new byte[] { 0 } });
        var mm = new ModelManager(new HttpClient(server));
        await mm.EnsureAsync(new ModelSpec("m.bin", "http://main/m", Sha(body), body.Length, FallbackUrls: new[] { "http://copy/m" }), _dir);
        Assert.Equal(new[] { "http://main/m" }, server.Asked);
    }

    [Fact]
    public async Task EveryAddressFails_ThrowsAndLeavesNothing()
    {
        var body = new byte[] { 1, 2, 3 };
        var server = new MultiServer(new() { ["http://main/m"] = null, ["http://copy/m"] = new byte[] { 5, 5, 5 } });
        var mm = new ModelManager(new HttpClient(server));
        var spec = new ModelSpec("m.bin", "http://main/m", Sha(body), body.Length, FallbackUrls: new[] { "http://copy/m" });
        await Assert.ThrowsAsync<InvalidDataException>(() => mm.EnsureAsync(spec, _dir));
        Assert.False(File.Exists(Path.Combine(_dir, "m.bin")));
        Assert.False(File.Exists(Path.Combine(_dir, "m.bin.part")));
    }

    [Fact]
    public async Task HalfFinishedDownload_IsNotMixedWithAnotherServersBytes()
    {
        var body = Enumerable.Range(0, 4000).Select(i => (byte)(i * 5)).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_dir, "m.bin.part"), body[..1000]);
        var server = new MultiServer(new() { ["http://main/m"] = null, ["http://copy/m"] = body });
        var mm = new ModelManager(new HttpClient(server));
        var path = await mm.EnsureAsync(new ModelSpec("m.bin", "http://main/m", Sha(body), body.Length, FallbackUrls: new[] { "http://copy/m" }), _dir);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task NameWithSubfolder_CreatesTheFolder()
    {
        var body = new byte[] { 1, 2, 3, 4, 5 };
        var mm = new ModelManager(new HttpClient(new FakeServer(body)));
        var path = await mm.EnsureAsync(new ModelSpec("parakeet-v2/encoder.int8.onnx", "http://x/m", Sha(body), body.Length), _dir);
        Assert.Equal(Path.Combine(_dir, "parakeet-v2", "encoder.int8.onnx"), path);
        Assert.Equal(body, await File.ReadAllBytesAsync(path));
    }
}
