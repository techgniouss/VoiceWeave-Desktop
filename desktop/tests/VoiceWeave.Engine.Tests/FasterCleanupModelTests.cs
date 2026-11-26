using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class FasterCleanupModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-faster-" + Guid.NewGuid().ToString("N"));

    public FasterCleanupModelTests() => Directory.CreateDirectory(Path.Combine(_dir, "cleanup-1"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private void MakeModel(long bytes, string? marker)
    {
        using (var f = File.Create(FasterCleanupModel.Path(_dir))) f.SetLength(bytes);
        if (marker != null) File.WriteAllText(FasterCleanupModel.Path(_dir) + ".from", marker);
    }

    [Fact]
    public void NotReady_WhenTheFileIsMissing() => Assert.False(FasterCleanupModel.IsReady(_dir));

    [Fact]
    public void NotReady_WithoutTheMarker()
    {
        MakeModel(250L * 1024 * 1024, null);
        Assert.False(FasterCleanupModel.IsReady(_dir));
    }

    [Fact]
    public void NotReady_WhenMadeFromADifferentModelFile()
    {
        MakeModel(250L * 1024 * 1024, "0000");
        Assert.False(FasterCleanupModel.IsReady(_dir));
    }

    [Fact]
    public void NotReady_WhenTheFileIsImplausiblySmall()
    {
        MakeModel(1024, LlamaCleaner.File.Sha256);
        Assert.False(FasterCleanupModel.IsReady(_dir));
    }

    [Fact]
    public void Ready_WhenTheFileIsBigAndMadeFromTheCurrentModel()
    {
        MakeModel(250L * 1024 * 1024, LlamaCleaner.File.Sha256);
        Assert.True(FasterCleanupModel.IsReady(_dir));
    }

    [Fact]
    public async Task EnsureAsync_WithoutTheStandardModel_SaysSo()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => FasterCleanupModel.EnsureAsync(_dir));
    }

    [Fact]
    public void SwitchingTheCleanerModel_BeforeItLoadsIsHarmless()
    {
        using var cleaner = new LlamaCleaner(_dir);
        cleaner.UseModel(FasterCleanupModel.Path(_dir));
        cleaner.UseModel(Path.Combine(_dir, LlamaCleaner.File.Name));
    }
}
