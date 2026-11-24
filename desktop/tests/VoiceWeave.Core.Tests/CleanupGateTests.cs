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
    [InlineData("i think we should ship this today")]
    public void NeedsCleanup_ForFillersNumbersEmailsCorrectionsOrMissingPunctuation(string text) =>
        Assert.True(CleanupGate.NeedsCleanup(text));

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
