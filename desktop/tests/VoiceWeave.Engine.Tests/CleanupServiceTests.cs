using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class CleanupServiceTests
{
    private sealed class FakeCleaner(Func<string, string> f) : ICleaner
    {
        public int Calls;
        public string Clean(string text, Tone tone) { Calls++; return f(text); }
        public void Dispose() { }
    }

    private static readonly IReadOnlyList<string> None = Array.Empty<string>();

    [Theory]
    [InlineData(Tone.Casual, "tone: casual\ntext: hello there\nout:\n")]
    [InlineData(Tone.Formal, "tone: formal\ntext: hello there\nout:\n")]
    [InlineData(Tone.VeryCasual, "tone: very casual\ntext: hello there\nout:\n")]
    public void Prompt_MatchesModelCard(Tone tone, string expected) =>
        Assert.Equal(expected, LlamaCleaner.BuildPrompt("hello there", tone));

    [Fact]
    public void CleanText_SkipsTheModel()
    {
        var c = new FakeCleaner(_ => "should not be used");
        var svc = new CleanupService(c);
        Assert.Equal("Please review the pull request.", svc.Run("Please review the pull request.", Tone.Casual, None));
        Assert.Equal(0, c.Calls);
    }

    [Fact]
    public void NoisyText_UsesModelOutput()
    {
        var c = new FakeCleaner(_ => "Send the report to Maria by Friday.");
        var svc = new CleanupService(c);
        Assert.Equal("Send the report to Maria by Friday.",
            svc.Run("send the report to john no wait to maria by friday", Tone.Casual, None));
        Assert.Equal(1, c.Calls);
    }

    [Fact]
    public void HmmStrippedBeforeTheModelSeesIt()
    {
        string? seen = null;
        var svc = new CleanupService(new FakeCleaner(t => { seen = t; return "I think so."; }));
        svc.Run("Hmm. i think you know so", Tone.Casual, None);
        Assert.Equal("i think you know so", seen);
    }

    [Fact]
    public void ChangedProtectedTerm_FallsBackToInput()
    {
        var svc = new CleanupService(new FakeCleaner(_ => "I'll be near Maupuri at six."));
        var result = svc.Run("i'll be near Myapur at six pm", Tone.Casual, new[] { "Myapur" });
        Assert.Equal("I'll be near Myapur at six pm.", result);
    }

    [Fact]
    public void RunawayOutput_FallsBackToInput()
    {
        var svc = new CleanupService(new FakeCleaner(_ => new string('x', 500)));
        Assert.Equal("Hello you know.", svc.Run("um hello you know", Tone.Casual, None)); // noise stripped, runaway output discarded
    }

    [Fact]
    public void ModelThrows_FallsBackToInput()
    {
        var svc = new CleanupService(new FakeCleaner(_ => throw new InvalidOperationException("boom")));
        Assert.Equal("Hello there friend you know.", svc.Run("hello there uh friend you know", Tone.Casual, None));
    }

    [Fact]
    public void EmptyModelOutput_FallsBackToInput()
    {
        var svc = new CleanupService(new FakeCleaner(_ => "  "));
        Assert.Equal("Send it now you know.", svc.Run("send it uh now you know", Tone.Casual, None));
    }
}
