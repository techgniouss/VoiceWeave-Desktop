using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class ToneByAppTests
{
    [Theory]
    [InlineData("slack", Tone.VeryCasual)]
    [InlineData("ms-teams", Tone.VeryCasual)]
    [InlineData("Discord", Tone.VeryCasual)]
    [InlineData("OUTLOOK", Tone.Formal)]
    [InlineData("olk", Tone.Formal)]
    [InlineData("WINWORD.EXE", Tone.Formal)]
    [InlineData("chrome", Tone.Casual)]
    [InlineData("notepad", Tone.Casual)]
    [InlineData("", Tone.Casual)]
    [InlineData(null, Tone.Casual)]
    public void PicksTheToneFromTheProgram(string? name, Tone expected) => Assert.Equal(expected, ToneByApp.For(name));
}

public class VocabularyCleanupTests
{
    private sealed class RecordingCleaner : ICleaner
    {
        public IReadOnlyList<string>? Vocabulary;
        public int Calls;
        public string Reply = "Send the report to Maria by Friday.";
        public string Clean(string text, Tone tone) { Calls++; Vocabulary = null; return Reply; }
        public string Clean(string text, Tone tone, IReadOnlyList<string>? vocabulary) { Calls++; Vocabulary = vocabulary; return Reply; }
        public void Dispose() { }
    }

    private const string Noisy = "send the report to john no wait to maria by friday";

    [Fact]
    public void Prompt_WithTerms_PutsThemBetweenToneAndText() =>
        Assert.Equal("tone: casual\nterms: Srikanth, Quarkus\ntext: hello there\nout:\n",
            LlamaCleaner.BuildPrompt("hello there", Tone.Casual, new[] { "Srikanth", "Quarkus" }));

    [Fact]
    public void Prompt_WithoutTerms_IsExactlyWhatItWasBefore()
    {
        Assert.Equal("tone: casual\ntext: hello there\nout:\n", LlamaCleaner.BuildPrompt("hello there", Tone.Casual));
        Assert.Equal("tone: casual\ntext: hello there\nout:\n", LlamaCleaner.BuildPrompt("hello there", Tone.Casual, Array.Empty<string>()));
    }

    [Fact]
    public void Off_ByDefault_NothingIsSentToTheModel()
    {
        var cleaner = new RecordingCleaner();
        new CleanupService(cleaner).Run(Noisy, Tone.Casual, new[] { "Srikanth" });
        Assert.Equal(1, cleaner.Calls);
        Assert.Null(cleaner.Vocabulary);
    }

    [Fact]
    public void On_SendsTheTerms()
    {
        var cleaner = new RecordingCleaner();
        new CleanupService(cleaner) { UseVocabulary = () => true }.Run(Noisy, Tone.Casual, new[] { "Srikanth", "Quarkus" });
        Assert.Equal(new[] { "Srikanth", "Quarkus" }, cleaner.Vocabulary);
    }

    [Fact]
    public void On_ButNoTerms_SendsNothing()
    {
        var cleaner = new RecordingCleaner();
        new CleanupService(cleaner) { UseVocabulary = () => true }.Run(Noisy, Tone.Casual, Array.Empty<string>());
        Assert.Null(cleaner.Vocabulary);
    }

    [Fact]
    public void ChoosesTheNewestTerms_WithinTheLimit()
    {
        var terms = Enumerable.Range(1, 50).Select(i => $"Term{i:00}").ToList();
        var picked = CleanupService.PickVocabulary(terms);
        Assert.Equal(CleanupService.MaxVocabularyTerms, picked.Count);
        Assert.Equal("Term31", picked[0]);   // the 20 newest, oldest of them first
        Assert.Equal("Term50", picked[^1]);
    }

    [Fact]
    public void StaysWithinTheCharacterBudget()
    {
        var terms = Enumerable.Range(1, 20).Select(i => new string((char)('A' + i), 30)).ToList();
        var picked = CleanupService.PickVocabulary(terms);
        Assert.True(string.Join(", ", picked).Length <= CleanupService.MaxVocabularyChars);
        Assert.True(picked.Count < 20);
    }

    [Fact]
    public void SkipsTermsThatWouldBreakTheList_AndDuplicates()
    {
        var picked = CleanupService.PickVocabulary(new[] { "Argo CD", "a, b", "x: y", "argo cd", "Flyway", "  " });
        Assert.Equal(new[] { "argo cd", "Flyway" }, picked); // the newest spelling of a duplicate is kept
    }

    [Fact]
    public void AnswerThatRepeatsTheWordList_IsRefused()
    {
        var cleaner = new RecordingCleaner { Reply = "terms: Srikanth, Quarkus text: send the report" };
        var result = new CleanupService(cleaner) { UseVocabulary = () => true }.Run(Noisy, Tone.Casual, new[] { "Srikanth", "Quarkus" });
        Assert.DoesNotContain("terms:", result);
        Assert.Contains("john", result); // the plain tidy-up of what was said
    }

    [Fact]
    public void TheSafetyChecksStillApply()
    {
        var cleaner = new RecordingCleaner { Reply = "Send the report to Maria by Friday." };
        var result = new CleanupService(cleaner) { UseVocabulary = () => true }.Run("send the report to Srikanth no wait to Maria by friday", Tone.Casual, new[] { "Srikanth" });
        Assert.Equal(1, cleaner.Calls);
        Assert.Contains("Srikanth", result); // the model dropped a protected term, so its answer was refused
    }
}
