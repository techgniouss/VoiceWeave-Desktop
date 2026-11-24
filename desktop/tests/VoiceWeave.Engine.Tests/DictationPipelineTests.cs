using VoiceWeave.Core;
using VoiceWeave.Engine;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.Engine.Tests;

public class DictationPipelineTests
{
    private sealed class FakeAsr(string text) : IAsr
    {
        public int Calls;
        public string Transcribe(float[] pcm) { Calls++; return text; }
        public void Dispose() { }
    }

    private sealed class FakeCleaner(Func<string, string> f) : ICleaner
    {
        public int Calls;
        public string Clean(string text, Tone tone) { Calls++; return f(text); }
        public void Dispose() { }
    }

    private static float[] Speech(double seconds) => Enumerable.Repeat(0.2f, (int)(seconds * 16000)).ToArray();

    [Fact]
    public void Silence_NoModelCalls_NoText()
    {
        var asr = new FakeAsr("hello");
        var p = new DictationPipeline(asr, new CleanupService(new FakeCleaner(t => t)), CoreDictionary.Parse(""));
        var r = p.Run(new float[16000], Tone.Casual);
        Assert.Equal("", r.Final);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public void UnderTwoHundredMs_Ignored()
    {
        var asr = new FakeAsr("hello");
        var p = new DictationPipeline(asr, new CleanupService(new FakeCleaner(t => t)), CoreDictionary.Parse(""));
        Assert.Equal("", p.Run(Speech(0.15), Tone.Casual).Final);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public void DictionaryRunsBeforeCleanup_AndProtectsItsTerms()
    {
        string? seenByCleaner = null;
        var cleaner = new FakeCleaner(t => { seenByCleaner = t; return "I'll be near Myapur at six."; });
        var dict = CoreDictionary.Parse("my pur => Myapur");
        var p = new DictationPipeline(new FakeAsr("i'll be near my pur at six"), new CleanupService(cleaner), dict);
        var r = p.Run(Speech(2), Tone.Casual);
        Assert.Equal("i'll be near Myapur at six", seenByCleaner);
        Assert.Equal("I'll be near Myapur at six.", r.Final);
        Assert.Equal("i'll be near my pur at six", r.Raw);
    }

    [Fact]
    public void CleanTranscript_SkipsCleanupModel()
    {
        var cleaner = new FakeCleaner(t => "unused");
        var p = new DictationPipeline(new FakeAsr("Please review the pull request."), new CleanupService(cleaner), CoreDictionary.Parse(""));
        Assert.Equal("Please review the pull request.", p.Run(Speech(2), Tone.Casual).Final);
        Assert.Equal(0, cleaner.Calls);
    }

    [Fact]
    public void AsrReturnsNothing_ReturnsEmpty()
    {
        var p = new DictationPipeline(new FakeAsr("  "), new CleanupService(new FakeCleaner(t => t)), CoreDictionary.Parse(""));
        Assert.Equal("", p.Run(Speech(2), Tone.Casual).Final);
    }
}
