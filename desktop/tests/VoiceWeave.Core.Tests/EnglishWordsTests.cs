namespace VoiceWeave.Core.Tests;

public class EnglishWordsTests
{
    [Fact]
    public void NormalEnglishSentence_IsFullyPlausible() =>
        Assert.True(EnglishWords.Plausibility("Please review the pull request after standup.") >= 0.99);

    [Fact]
    public void TeluguHeardAsEnglishNonsense_IsNotPlausible() =>
        Assert.True(EnglishWords.Plausibility("Aim Chesto naura") <= 0.40);

    [Fact]
    public void EmptyText_IsNotPlausible() => Assert.Equal(0.0, EnglishWords.Plausibility(""));

    [Fact]
    public void OnlyPunctuationOrDigits_IsNotPlausible()
    {
        Assert.Equal(0.0, EnglishWords.Plausibility("... ?!"));
        Assert.Equal(0.0, EnglishWords.Plausibility("12 345"));
    }

    [Fact]
    public void NamesNotInTheDictionary_LowerTheScore_ButKnownTermsCountAsWords()
    {
        var plain = EnglishWords.Plausibility("Quarkus deployed Miyapur");
        var withTerms = EnglishWords.Plausibility("Quarkus deployed Miyapur", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Miyapur", "Quarkus" });
        Assert.True(plain < 0.5);
        Assert.Equal(1.0, withTerms, 3);
    }

    [Fact]
    public void CaseAndPunctuationDoNotMatter() =>
        Assert.Equal(EnglishWords.Plausibility("hello there friend"), EnglishWords.Plausibility("Hello, THERE! Friend?"));

    [Fact]
    public void ApostrophesAndPossessives_AreHandled()
    {
        Assert.True(EnglishWords.Plausibility("I'll meet John's friend") >= 0.7);
        Assert.True(EnglishWords.Plausibility("don't worry") >= 0.99);
    }

    [Fact]
    public void LoneLettersAreIgnored_SoNoiseCannotInflateTheScore() =>
        Assert.True(EnglishWords.Plausibility("b c d x y") <= 0.2);

    [Fact]
    public void TheWordsAAndI_Count() => Assert.True(EnglishWords.Plausibility("I have a plan") >= 0.99);
}
