namespace VoiceWeave.Core.Tests;

public class ScoringTests
{
    [Fact]
    public void Identical_ZeroErrors() =>
        Assert.Equal((0, 3), Scoring.Wer("send the report", "send the report"));

    [Fact]
    public void OneSubstitution() =>
        Assert.Equal((1, 3), Scoring.Wer("send the report", "send a report"));

    [Fact]
    public void CaseAndPunctuationIgnored() =>
        Assert.Equal((0, 4), Scoring.Wer("Push the pull-request.", "push the pull request"));

    [Fact]
    public void DeletionsAndInsertionsCounted()
    {
        Assert.Equal((1, 3), Scoring.Wer("a b c", "a c"));
        Assert.Equal((1, 3), Scoring.Wer("a b c", "a b x c"));
    }

    [Fact]
    public void Cer_CountsCharacters() =>
        Assert.Equal((1, 5), Scoring.Cer("hello", "hallo"));

    [Fact]
    public void TermAccuracy_IsCaseSensitive()
    {
        Assert.Equal((1, 2), Scoring.TermAccuracy(new[] { "Quarkus", "Argo CD" }, "deployed quarkus with Argo CD"));
    }
}
