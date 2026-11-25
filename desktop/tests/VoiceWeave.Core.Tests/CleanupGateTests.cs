namespace VoiceWeave.Core.Tests;

public class CleanupGateTests
{
    [Fact]
    public void CleanShortSentence_SkipsCleanup() =>
        Assert.False(CleanupGate.NeedsCleanup("Please review the pull request after standup."));

    [Theory]
    [InlineData("So I think uh we should go")]
    [InlineData("the invoice is due in thirty days")]
    [InlineData("email me at emily dot carter at example dot com")]
    [InlineData("send it to john no wait to maria")]
    [InlineData("we need two hundred and fifty users")]
    [InlineData("the meeting is at three pm")]
    [InlineData("it grew ten percent")]
    [InlineData("can you check the build")]
    [InlineData("what time is it")]
    public void NeedsCleanup_ForFillersNumbersEmailsCorrectionsOrUnpunctuatedQuestions(string text) =>
        Assert.True(CleanupGate.NeedsCleanup(text));

    [Theory]
    [InlineData("i think we should ship this today")]
    [InlineData("Send the report to Maria")]
    [InlineData("one of the options is better")]
    [InlineData("I have two of them")]
    [InlineData("yeah that works for me thanks")]
    public void NeedsCleanup_NotForPlainTextWhereOnlyTheCapitalOrFullStopIsMissing(string text) =>
        Assert.False(CleanupGate.NeedsCleanup(text));

    [Theory]
    [InlineData("i think we should ship this today", "I think we should ship this today.")]
    [InlineData("Already fine.", "Already fine.")]
    [InlineData("Is that right?", "Is that right?")]
    [InlineData("send it now", "Send it now.")]
    [InlineData("iPhone prices dropped", "iPhone prices dropped.")]
    [InlineData("3 apples please", "3 apples please.")]
    public void Tidy_AddsCapitalAndFullStop(string input, string expected) =>
        Assert.Equal(expected, CleanupGate.Tidy(input));

    [Fact]
    public void Tidy_KeepsProtectedTermSpelling() =>
        Assert.Equal("npm install works.", CleanupGate.Tidy("npm install works", new[] { "npm" }));

    [Fact]
    public void StripNoise_RemovesHmm() =>
        Assert.Equal("So I think", CleanupGate.StripNoise("Hmm. So I think"));

    [Fact]
    public void StripNoise_RemovesStandaloneFillers_NotWordsContainingThem() =>
        Assert.Equal("The umbrella is here", CleanupGate.StripNoise("The umbrella is here um"));

    [Fact]
    public void ChangedProtectedTerm_Rejected() =>
        Assert.False(CleanupGate.ProtectedTermsIntact("I'll be near Myapur at six", "I'll be near Maupuri at six.", new[] { "Myapur" }));

    [Fact]
    public void KeptProtectedTerm_Accepted() =>
        Assert.True(CleanupGate.ProtectedTermsIntact("I'll be near Myapur at six", "I'll be near Myapur at six.", new[] { "Myapur" }));

    [Fact]
    public void ProtectedTermAbsentFromInput_IsIgnored() =>
        Assert.True(CleanupGate.ProtectedTermsIntact("hello there", "Hello there.", new[] { "Quarkus" }));
}
