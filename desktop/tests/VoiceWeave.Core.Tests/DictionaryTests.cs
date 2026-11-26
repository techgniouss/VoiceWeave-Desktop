namespace VoiceWeave.Core.Tests;

public class DictionaryTests
{
    [Fact]
    public void ExactPhrase_ReplacedCaseInsensitively()
    {
        var d = Dictionary.Parse("quark us => Quarkus");
        Assert.Equal("Quarkus deployed", d.Apply("Quark Us deployed"));
    }

    [Fact]
    public void Fuzzy_SingleWord_WithinDistanceTwo()
    {
        var d = Dictionary.Parse("srikanth => Srikanth");
        Assert.Equal("Srikanth deployed it", d.Apply("Srikunth deployed it"));
    }

    [Fact]
    public void Fuzzy_RequiresSameFirstLetter_AndLengthWithinOne()
    {
        var d = Dictionary.Parse("srikanth => Srikanth");
        Assert.Equal("Sri went", d.Apply("Sri went"));
        Assert.Equal("Srikanthan went", d.Apply("Srikanthan went"));
        Assert.Equal("Krikanth went", d.Apply("Krikanth went"));
    }

    [Fact]
    public void Fuzzy_LeavesRealEnglishWordsAlone()
    {
        var d = Dictionary.Parse("marks => Marks");
        Assert.Equal("the masks were here", d.Apply("the masks were here"));
        Assert.Equal("Marks were here", d.Apply("Mraks were here")); // a typo-like near miss still gets fixed
    }

    [Fact]
    public void Apply_ReportsTheRulesThatFired()
    {
        var d = Dictionary.Parse("sri kanth => Srikanth\nquark us => Quarkus");
        var fired = new List<AppliedRule>();
        d.Apply("ask sri kanth about it", fired);
        var rule = Assert.Single(fired);
        Assert.Equal("sri kanth => Srikanth", rule.Line);
        Assert.Equal("Srikanth", rule.Preferred);
    }

    [Fact]
    public void Apply_ReportsAFuzzyMatchToo()
    {
        var d = Dictionary.Parse("srikanth => Srikanth");
        var fired = new List<AppliedRule>();
        d.Apply("Srikunth deployed it", fired);
        Assert.Equal("srikanth => Srikanth", Assert.Single(fired).Line);
    }

    [Fact]
    public void NoReplacementInsideLongerWords()
    {
        var d = Dictionary.Parse("cat => Kat");
        Assert.Equal("concatenate the Kat", d.Apply("concatenate the cat"));
    }

    [Fact]
    public void MultiWordPhrase_Replaced()
    {
        var d = Dictionary.Parse("sri kantur => Srikanth");
        Assert.Equal("Please send it to Srikanth now", d.Apply("Please send it to sri kantur now"));
    }

    [Fact]
    public void CommentsAndBlankLines_Ignored_AndTermsListed()
    {
        var d = Dictionary.Parse("# note\n\nargo cd => Argo CD\nflyway => Flyway");
        Assert.Equal(new[] { "Argo CD", "Flyway" }, d.Terms);
    }

    [Fact]
    public void PunctuationPreserved()
    {
        var d = Dictionary.Parse("quark us => Quarkus");
        Assert.Equal("Deploy Quarkus, then stop.", d.Apply("Deploy quark us, then stop."));
    }

    [Fact]
    public void IndicScriptWords_AreMatchedWhole_IncludingVowelSigns()
    {
        // Telugu "Hyderabad" (హైదరాబాద్) and a dictionary entry that rewrites it; vowel signs must not split the word.
        var d = Dictionary.Parse("హైదరాబాద్ => Hyderabad");
        Assert.Equal("నేను రేపు Hyderabad వెళ్తున్నాను", d.Apply("నేను రేపు హైదరాబాద్ వెళ్తున్నాను"));
    }

    [Fact]
    public void IndicWord_NotReplacedInsideALongerWord()
    {
        var d = Dictionary.Parse("కల => Kala");
        Assert.Equal("కలకత్తా", d.Apply("కలకత్తా"));
    }
}
