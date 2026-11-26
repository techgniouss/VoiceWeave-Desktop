namespace VoiceWeave.Core.Tests;

public class SoundAlikeTests
{
    [Theory]
    [InlineData("Srikanth", "Sreekant")]
    [InlineData("Srikanth", "sri kant")]
    [InlineData("Quarkus", "quark us")]
    [InlineData("Quarkus", "kwarkus")]
    [InlineData("Flyway", "fly way")]
    public void SameKey_ForWordsThatSoundTheSame(string a, string b) =>
        Assert.Equal(SoundAlike.Key(SoundAlike.Squash(a)), SoundAlike.Key(SoundAlike.Squash(b)));

    [Theory]
    [InlineData("Srikanth", "Srinivas")]
    [InlineData("Marks", "Makers")]
    [InlineData("Kubernetes", "Quarkus")]
    public void DifferentKey_ForWordsThatDoNot(string a, string b) =>
        Assert.NotEqual(SoundAlike.Key(SoundAlike.Squash(a)), SoundAlike.Key(SoundAlike.Squash(b)));

    [Fact]
    public void OtherScripts_HaveNoKey() => Assert.Equal("", SoundAlike.Key(SoundAlike.Squash("హైదరాబాద్")));

    [Fact]
    public void Squash_DropsSpacesAndPunctuation() => Assert.Equal("argocd", SoundAlike.Squash("Argo C. D"));
}

public class DictionarySoundAlikeTests
{
    private static Dictionary Dict(string rules) => Dictionary.Parse(rules);

    [Theory]
    [InlineData("ask Sreekant about it", "ask Srikanth about it")]
    [InlineData("ask sri kant about it", "ask Srikanth about it")]
    [InlineData("ask Sri Kanth about it", "ask Srikanth about it")]
    [InlineData("ask srikant about it", "ask Srikanth about it")]
    public void NameHeardDifferently_IsWrittenTheLearnedWay(string heard, string expected)
    {
        var d = Dict("srikanth => Srikanth");
        Assert.Equal(expected, d.Apply(heard));
    }

    [Fact]
    public void SpacingVariants_OfAMultiWordTerm_AreFixed()
    {
        var d = Dict("argo cd => Argo CD");
        Assert.Equal("sync Argo CD now", d.Apply("sync argo c d now"));
        Assert.Equal("sync Argo CD now", d.Apply("sync argocd now"));
    }

    [Fact]
    public void ThreeSpokenWords_CanBeOneTerm()
    {
        var d = Dict("srikanth => Srikanth");
        Assert.Equal("ask Srikanth", d.Apply("ask sri kan th"));
    }

    [Fact]
    public void AlreadyRight_IsLeftAloneAndNotReportedAsFired()
    {
        var d = Dict("argo see dee => Argo CD"); // no exact rule applies, so only the sound-alike path sees "Argo CD"
        var fired = new List<AppliedRule>();
        Assert.Equal("sync Argo CD now", d.Apply("sync Argo CD now", fired));
        Assert.Empty(fired);
    }

    [Fact]
    public void RealEnglishWords_AreNeverRewritten()
    {
        var d = Dict("stripe => Stripe\nmarks => Marks");
        Assert.Equal("strip the file and the makers", d.Apply("strip the file and the makers"));
    }

    [Fact]
    public void DifferentSounds_AreNotMerged()
    {
        var d = Dict("srikanth => Srikanth");
        Assert.Equal("ask Srinivas about it", d.Apply("ask Srinivas about it"));
    }

    [Fact]
    public void PunctuationBetweenWords_BreaksASoundAlikePhrase()
    {
        var d = Dict("argo cd => Argo CD");
        Assert.Equal("argo, c d", d.Apply("argo, c d"));
    }

    [Fact]
    public void ReportsTheRuleThatWasUsed()
    {
        var d = Dict("srikanth => Srikanth");
        var fired = new List<AppliedRule>();
        d.Apply("ask Sreekant", fired);
        Assert.Equal("srikanth => Srikanth", Assert.Single(fired).Line);
    }

    [Fact]
    public void ShortTerms_AreLeftToExactRules()
    {
        var d = Dict("k8s => k8s\nai => AI");
        Assert.Equal("kate s and ay", d.Apply("kate s and ay"));
    }

    [Fact]
    public void IndicWords_AreNeverMatchedBySound()
    {
        // Dropping the vowel signs would make different Telugu words look the same, so they have no sound form at all.
        Assert.Equal("", SoundAlike.Squash("హిదరాబాద"));
        Assert.Equal("", SoundAlike.Squash("హైదరాబాద్"));
        Assert.Equal("", SoundAlike.Key(SoundAlike.Squash("హైదరాబాద్")));
        var d = Dict("హైదరాబాద్ => హైదరాబాద్");
        Assert.Equal("నేను హదరబద", d.Apply("నేను హదరబద")); // same base letters, different word: left alone
    }

    [Fact]
    public void Accents_AreLeftToExactRules() => Assert.Equal("", SoundAlike.Squash("Müller"));

    [Fact]
    public void TurnedOff_OnlyExactAndNearMissRulesApply()
    {
        var d = Dictionary.Parse("srikanth => Srikanth\nargo cd => Argo CD", soundAlike: false);
        Assert.Equal("ask Sreekant", d.Apply("ask Sreekant"));
        Assert.Equal("sync argo c d", d.Apply("sync argo c d"));
        Assert.Equal("ask Srikanth", d.Apply("ask srikunth")); // the older near-miss matching still works
    }

    [Fact]
    public void FiredRules_SayWhetherTheyMatchedExactlyOrByGuess()
    {
        var d = Dict("srikanth => Srikanth\nquark us => Quarkus");
        var fired = new List<AppliedRule>();
        d.Apply("ask Sreekant and quark us", fired);
        Assert.False(fired.Single(f => f.Preferred == "Quarkus").Approximate);
        Assert.True(fired.Single(f => f.Preferred == "Srikanth").Approximate);
    }

    [Fact]
    public void AnExactHit_BeatsAnEarlierGuess_ForTheSameRule()
    {
        var d = Dict("srikanth => Srikanth");
        var fired = new List<AppliedRule>();
        d.Apply("Sreekant and srikanth", fired);
        Assert.False(Assert.Single(fired).Approximate);
    }

    [Fact]
    public void ExactRulesStillWinOverSoundAlikes()
    {
        var d = Dict("sri kanth => Shrikanth\nsrikanth => Srikanth");
        Assert.Equal("Shrikanth and Srikanth", d.Apply("sri kanth and srikanth"));
    }
}
