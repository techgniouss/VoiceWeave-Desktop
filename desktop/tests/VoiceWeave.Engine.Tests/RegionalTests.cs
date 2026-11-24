using System.Net;
using System.Security.Cryptography;
using System.Text;
using VoiceWeave.Core;
using VoiceWeave.Engine;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.Engine.Tests;

public class LanguageTests
{
    [Theory]
    [InlineData(Language.English, "en", "English")]
    [InlineData(Language.Telugu, "te", "Telugu")]
    [InlineData(Language.Hindi, "hi", "Hindi")]
    [InlineData(Language.Tamil, "ta", "Tamil")]
    public void CodesAndNames(Language l, string code, string name)
    {
        Assert.Equal(code, Languages.Code(l));
        Assert.Equal(name, Languages.DisplayName(l));
        Assert.Equal(l, Languages.Parse(code));
    }

    [Fact]
    public void OnlyEnglishUsesTheCleanupModel()
    {
        Assert.True(Languages.UsesCleanup(Language.English));
        Assert.False(Languages.UsesCleanup(Language.Telugu));
        Assert.False(Languages.UsesCleanup(Language.Hindi));
        Assert.False(Languages.UsesCleanup(Language.Tamil));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    public void UnknownCode_FallsBackToEnglish(string? code) => Assert.Equal(Language.English, Languages.Parse(code));
}

public class OnnxMetadataTests
{
    [Fact]
    public void Encode_MatchesTheProtobufFieldLayoutUsedByTheAndroidApp()
    {
        var bytes = OnnxMetadata.Encode(new[] { ("vocab_size", "257") });
        var expected = new List<byte> { 0x72, (byte)(4 + 10 + 3), 0x0A, 10 };
        expected.AddRange(Encoding.UTF8.GetBytes("vocab_size"));
        expected.AddRange(new byte[] { 0x12, 3 });
        expected.AddRange(Encoding.UTF8.GetBytes("257"));
        Assert.Equal(expected.ToArray(), bytes);
    }

    [Fact]
    public void Encode_RejectsEntriesTooLongForOneByteLengths() =>
        Assert.Throws<ArgumentException>(() => OnnxMetadata.Encode(new[] { ("k", new string('x', 130)) }));

    [Fact]
    public async Task Append_AddsTheEntriesToTheEndOfTheFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            await OnnxMetadata.AppendAsync(path, new[] { ("a", "b") });
            var all = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 1, 2, 3 }, all[..3]);
            Assert.Equal(OnnxMetadata.Encode(new[] { ("a", "b") }), all[3..]);
        }
        finally { File.Delete(path); }
    }
}

public class EngineManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-eng-" + Guid.NewGuid().ToString("N"));
    public EngineManagerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private sealed class Files(Dictionary<string, byte[]> bodies) : HttpMessageHandler
    {
        public int Requests;
        public bool FailNext;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if (FailNext) { FailNext = false; throw new HttpRequestException("offline"); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bodies[r.RequestUri!.AbsolutePath]) });
        }
    }

    private sealed class FakeAsr : IAsr
    {
        public static int Warmups;
        public string Transcribe(float[] pcm) { Interlocked.Increment(ref Warmups); return "x"; }
        public void Dispose() { }
    }

    private (EngineManager manager, Files http, int[] created) Rig()
    {
        var a = new byte[] { 1, 2, 3 };
        var b = new byte[] { 4, 5, 6, 7 };
        var http = new Files(new() { ["/a"] = a, ["/b"] = b });
        var created = new int[1];
        var packs = new Dictionary<Language, LanguagePack>
        {
            [Language.Telugu] = new(Language.Telugu,
                new[] { new ModelSpec("te/a.bin", "http://h/a", Sha(a), a.Length), new ModelSpec("te/b.bin", "http://h/b", Sha(b), b.Length) },
                dir => { created[0]++; return new FakeAsr(); }),
        };
        var manager = new EngineManager(new ModelManager(new HttpClient(http)), _dir, l => packs[l]);
        return (manager, http, created);
    }

    [Fact]
    public async Task EnsureReady_DownloadsEachFileOnce_AndLoadsTheEngine()
    {
        var (m, http, created) = Rig();
        await m.EnsureReadyAsync(Language.Telugu);
        Assert.Equal(2, http.Requests);
        Assert.Equal(1, created[0]);
        Assert.NotNull(m.Get(Language.Telugu));
    }

    [Fact]
    public async Task LoadingAnEngine_WarmsItUpOnce_SoTheFirstRealSentenceIsNotSlow()
    {
        var (m, _, _) = Rig();
        var before = FakeAsr.Warmups;
        await m.EnsureReadyAsync(Language.Telugu);
        await m.EnsureReadyAsync(Language.Telugu);
        Assert.Equal(1, FakeAsr.Warmups - before);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneDownload()
    {
        var (m, http, created) = Rig();
        await Task.WhenAll(m.EnsureReadyAsync(Language.Telugu), m.EnsureReadyAsync(Language.Telugu), m.EnsureReadyAsync(Language.Telugu));
        Assert.Equal(2, http.Requests);
        Assert.Equal(1, created[0]);
    }

    [Fact]
    public async Task AfterAFailure_TheNextCallRetries()
    {
        var (m, http, created) = Rig();
        http.FailNext = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => m.EnsureReadyAsync(Language.Telugu));
        await m.EnsureReadyAsync(Language.Telugu);
        Assert.Equal(1, created[0]);
    }

    [Fact]
    public async Task Progress_RunsFromZeroToOne_AcrossAllFiles()
    {
        var (m, _, _) = Rig();
        var seen = new List<double>();
        await m.EnsureReadyAsync(Language.Telugu, new Progress<double>(seen.Add));
        await Task.Delay(50); // Progress<T> posts asynchronously
        Assert.NotEmpty(seen);
        Assert.Equal(1.0, seen.Max(), 3);
        Assert.All(seen, p => Assert.InRange(p, 0.0, 1.0));
    }

    [Fact]
    public void Get_BeforeReady_Throws()
    {
        var (m, _, _) = Rig();
        Assert.Throws<InvalidOperationException>(() => m.Get(Language.Telugu));
    }
}

public class ModelTransformTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-tr-" + Guid.NewGuid().ToString("N"));
    public ModelTransformTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Counting(byte[] body) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task Transform_RunsAfterTheHashCheck_AndTheInstalledFileIsNotRedownloaded()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var sha = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var http = new Counting(body);
        var mm = new ModelManager(new HttpClient(http));
        var spec = new ModelSpec("m.bin", "http://h/m", sha, body.Length,
            Transform: p => OnnxMetadata.AppendAsync(p, new[] { ("k", "v") }),
            InstalledSize: body.Length + OnnxMetadata.Encode(new[] { ("k", "v") }).Length);
        var path = await mm.EnsureAsync(spec, _dir);
        Assert.Equal(spec.InstalledSize, new FileInfo(path).Length);
        await mm.EnsureAsync(spec, _dir);
        Assert.Equal(1, http.Requests);
    }
}
