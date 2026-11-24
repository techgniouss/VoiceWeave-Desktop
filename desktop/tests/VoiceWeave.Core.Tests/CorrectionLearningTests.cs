namespace VoiceWeave.Core.Tests;

// Cases ported from the Android app's tests for learning from the user's corrections.
public class CorrectionLearningTests
{
    private static string Pair(LearnedEdit e) => $"{e.OldWord} => {e.NewWord}";

    [Fact]
    public void MergedWords_BecomeAGeneralWordLevelRule()
    {
        var edits = CorrectionLearning.SuggestAll("please send it to sri kanth tomorrow", "please send it to Srikanth tomorrow");
        var edit = Assert.Single(edits);
        Assert.Equal("sri kanth => Srikanth", Pair(edit));
        Assert.True(edit.General);
    }

    [Fact]
    public void CaseOnlyChange_IsLearned() =>
        Assert.Equal("quarkus => Quarkus", Pair(Assert.Single(CorrectionLearning.SuggestAll("we use quarkus here", "we use Quarkus here"))));

    [Fact]
    public void Digits_AreLearned() =>
        Assert.Equal("kate s => k8s", Pair(Assert.Single(CorrectionLearning.SuggestAll("deploy on kate s today", "deploy on k8s today"))));

    [Fact]
    public void Hyphen_IsLearned() =>
        Assert.Equal("e mail => e-mail", Pair(Assert.Single(CorrectionLearning.SuggestAll("send an e mail now", "send an e-mail now"))));

    [Fact]
    public void TwoFixesInOneText_AreBothFound() =>
        Assert.Equal(2, CorrectionLearning.SuggestAll("tell sri kanth about quark us", "tell Srikanth about Quarkus").Count);

    [Fact]
    public void PlainWordSwap_OnlyMakesSenseInItsPhrase()
    {
        var edit = Assert.Single(CorrectionLearning.SuggestAll("the cat sat", "the cut sat"));
        Assert.False(edit.General);
        Assert.Equal("the cat sat => the cut sat", $"{edit.Before} => {edit.After}");
    }

    [Fact]
    public void CommonWords_AreNeverGeneral() =>
        Assert.False(CorrectionLearning.SuggestAll("go too the shop", "go to the shop")[0].General);

    [Fact]
    public void NoChange_NothingLearned() =>
        Assert.Empty(CorrectionLearning.SuggestAll("same text", "same text"));

    [Fact]
    public void Telugu_FixIsFound() =>
        Assert.Single(CorrectionLearning.SuggestAll("నేను ఇంటికి వెళ్తున్నాను", "నేను ఇంటికి వెళ్తాను"));

    [Fact]
    public void BigRewrites_AreIgnored_NotLearned()
    {
        // four or more words replaced at once is a rewrite, not a fix to one misheard word
        Assert.Empty(CorrectionLearning.SuggestAll("we will meet tomorrow at noon sharp", "see you all next week for lunch"));
    }

    [Fact]
    public void PureInsertionsAndDeletions_AreNotRules()
    {
        Assert.Empty(CorrectionLearning.SuggestAll("send the report", "send the final report"));
        Assert.Empty(CorrectionLearning.SuggestAll("send the final report", "send the report"));
    }

    [Fact]
    public void ArrowInTheText_IsNeverLearned() =>
        Assert.Empty(CorrectionLearning.SuggestAll("a b c", "a => c"));

    [Fact]
    public void EmptySides_GiveNothing()
    {
        Assert.Empty(CorrectionLearning.SuggestAll("", "hello"));
        Assert.Empty(CorrectionLearning.SuggestAll("hello", ""));
    }
}

public class LearningWatchTests
{
    [Fact]
    public void FindsTheDictatedRegion_InsideSurroundingText()
    {
        var watch = LearningWatch.TryCreate("Dear team, please send it to Srikanth tomorrow. Thanks", "please send it to Srikanth tomorrow.")!;
        Assert.Equal("please send it to Srikanth tomorrow.", watch.RegionIn("Dear team, please send it to Srikanth tomorrow. Thanks"));
    }

    [Fact]
    public void EditsInsideTheRegion_AreSeenAsTheNewRegion()
    {
        var watch = LearningWatch.TryCreate("Dear team, please send it to sri kanth tomorrow. Thanks", "please send it to sri kanth tomorrow.")!;
        Assert.Equal("please send it to Srikanth tomorrow.", watch.RegionIn("Dear team, please send it to Srikanth tomorrow. Thanks"));
    }

    [Fact]
    public void TextTypedAfterTheRegion_IsOnlyAnInsertion_SoNothingIsLearned()
    {
        var watch = LearningWatch.TryCreate("Hello world.", "Hello world.")!;
        var region = watch.RegionIn("Hello world. And more typed later")!;
        Assert.Empty(CorrectionLearning.SuggestAll(watch.Dictated, region));
    }

    [Fact]
    public void WorksWhenTheFieldHoldsOnlyTheDictation()
    {
        var watch = LearningWatch.TryCreate("hello there", "hello there")!;
        Assert.Equal("hello there", watch.RegionIn("hello there"));
        Assert.Equal("hallo there", watch.RegionIn("hallo there"));
    }

    [Fact]
    public void RegionLost_WhenTheSurroundingTextIsRewritten()
    {
        var watch = LearningWatch.TryCreate("Dear team, please send it. Thanks", "please send it.")!;
        Assert.Null(watch.RegionIn("Something else entirely"));
    }

    [Fact]
    public void DictationNotInTheField_GivesNoWatch() =>
        Assert.Null(LearningWatch.TryCreate("some other text", "not here"));

    [Fact]
    public void EmptyDictation_GivesNoWatch() => Assert.Null(LearningWatch.TryCreate("text", ""));

    [Fact]
    public void Rebase_MakesLaterComparisonsStartFromTheCorrectedText()
    {
        var watch = LearningWatch.TryCreate("a sri kanth b", "sri kanth")!;
        watch.Rebase("Srikanth");
        Assert.Equal("Srikanth", watch.Dictated);
    }

    [Fact]
    public void LongSurroundingText_IsAnchoredByItsNearestCharacters()
    {
        var before = string.Concat(Enumerable.Repeat("lorem ipsum ", 30));
        var after = string.Concat(Enumerable.Repeat(" dolor sit", 30));
        var watch = LearningWatch.TryCreate(before + "send it to sri kanth" + after, "send it to sri kanth")!;
        Assert.Equal("send it to Srikanth", watch.RegionIn(before + "send it to Srikanth" + after));
    }
}

public class DictionaryRulesTests
{
    private static LearnedEdit Edit(string before, string after) => CorrectionLearning.SuggestAll(before, after).Single();

    [Fact]
    public void GeneralFix_AddsAWordLevelLine()
    {
        var result = DictionaryRules.TryLearn("", Edit("we use quarkus here", "we use Quarkus here"));
        Assert.Equal("quarkus => Quarkus", result.Line);
    }

    [Fact]
    public void ContextOnlyFix_AddsTheWholePhrase()
    {
        var result = DictionaryRules.TryLearn("", Edit("the cat sat", "the cut sat"));
        Assert.Equal("the cat sat => the cut sat", result.Line);
    }

    [Fact]
    public void AlreadyKnown_IsNotAddedAgain()
    {
        var result = DictionaryRules.TryLearn("quarkus => Quarkus", Edit("we use quarkus here", "we use Quarkus here"));
        Assert.Null(result.Line);
        Assert.Contains("Already", result.Message);
    }

    [Fact]
    public void PhraseThatAlreadyHasADifferentRule_IsLeftAlone()
    {
        var result = DictionaryRules.TryLearn("sri kanth => Shrikanth", Edit("send to sri kanth now", "send to Srikanth now"));
        Assert.Null(result.Line);
        Assert.Contains("already has a rule", result.Message);
    }

    [Fact]
    public void FullDictionary_RefusesMore()
    {
        var big = string.Join('\n', Enumerable.Range(0, 700).Select(i => $"word{i} => Word{i}"));
        var result = DictionaryRules.TryLearn(big, Edit("we use quarkus here", "we use Quarkus here"));
        Assert.Null(result.Line);
        Assert.Contains("full", result.Message);
    }

    [Fact]
    public void RemoveLines_PreservesOlderAndLaterRules() =>
        Assert.Equal("sri kanth => Srikanth\nArgo CD",
            DictionaryRules.RemoveLines("sri kanth => Srikanth\nai => AI\nArgo CD", new[] { "ai => AI" }));

    [Fact]
    public void RemoveLines_RemovesOnlyOneExactCopy() =>
        Assert.Equal("ai => AI\nai => Ai",
            DictionaryRules.RemoveLines("ai => AI\nai => Ai\nai => AI", new[] { "ai => AI" }));

    [Fact]
    public void RemoveLines_KeepsARuleEditedAfterLearning() =>
        Assert.Equal("ai => A.I.", DictionaryRules.RemoveLines("ai => A.I.", new[] { "ai => AI" }));

    [Fact]
    public void RemoveLines_AllLearnedRules() =>
        Assert.Equal("", DictionaryRules.RemoveLines("ai => AI\ne mail => e-mail", new[] { "ai => AI", "e mail => e-mail" }));
}
