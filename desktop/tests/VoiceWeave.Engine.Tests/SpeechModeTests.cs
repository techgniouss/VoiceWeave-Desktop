using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class SpeechModeTests
{
    [Theory]
    [InlineData(SpeechMode.English, "en", "English")]
    [InlineData(SpeechMode.Telugu, "te", "Telugu")]
    [InlineData(SpeechMode.EnglishTelugu, "en+te", "English + Telugu (auto)")]
    [InlineData(SpeechMode.EnglishHindi, "en+hi", "English + Hindi (auto)")]
    [InlineData(SpeechMode.EnglishTamil, "en+ta", "English + Tamil (auto)")]
    public void CodesAndNames(SpeechMode mode, string code, string name)
    {
        Assert.Equal(code, SpeechModes.Code(mode));
        Assert.Equal(name, SpeechModes.DisplayName(mode));
        Assert.Equal(mode, SpeechModes.Parse(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xx")]
    public void UnknownCode_FallsBackToEnglish(string? code) => Assert.Equal(SpeechMode.English, SpeechModes.Parse(code));

    [Fact]
    public void OldSettingsValues_StillParse()
    {
        // settings.json written before the auto modes existed holds plain language codes
        Assert.Equal(SpeechMode.Telugu, SpeechModes.Parse("te"));
        Assert.Equal(SpeechMode.Hindi, SpeechModes.Parse("hi"));
    }

    [Fact]
    public void AutoModes_NeedBothLanguagesAndLanguageId()
    {
        Assert.True(SpeechModes.IsAuto(SpeechMode.EnglishTelugu));
        Assert.False(SpeechModes.IsAuto(SpeechMode.Telugu));
        Assert.Equal(new[] { Language.English, Language.Telugu }, SpeechModes.Needs(SpeechMode.EnglishTelugu));
        Assert.Equal(new[] { Language.Hindi }, SpeechModes.Needs(SpeechMode.Hindi));
        Assert.Equal(Language.Tamil, SpeechModes.Partner(SpeechMode.EnglishTamil));
        Assert.Null(SpeechModes.Partner(SpeechMode.English));
    }

    [Fact]
    public void SingleModes_HaveTheirOwnLanguage()
    {
        Assert.Equal(Language.Hindi, SpeechModes.Primary(SpeechMode.Hindi));
        Assert.Equal(Language.English, SpeechModes.Primary(SpeechMode.EnglishHindi));
    }
}

public class LanguageChooserTests
{
    [Theory]
    [InlineData("en", Language.English)]
    [InlineData("te", Language.Telugu)]
    [InlineData("hi", Language.Telugu)]   // Whisper often mislabels one Indian language as another: any Indian code means the partner
    [InlineData("ml", Language.Telugu)]
    [InlineData("kn", Language.Telugu)]
    [InlineData("ur", Language.Telugu)]
    public void KnownCodes(string code, Language expected) =>
        Assert.Equal(expected, LanguageChooser.Choose(code, Language.Telugu, seconds: 3, previous: null));

    [Theory]
    [InlineData("es")]
    [InlineData("ko")]
    [InlineData("")]
    public void UnrelatedCodes_KeepThePreviousLanguage_OrEnglish(string code)
    {
        Assert.Equal(Language.Telugu, LanguageChooser.Choose(code, Language.Telugu, 3, previous: Language.Telugu));
        Assert.Equal(Language.English, LanguageChooser.Choose(code, Language.Telugu, 3, previous: null));
    }

    [Fact]
    public void VeryShortSegments_InheritThePreviousLanguage()
    {
        Assert.Equal(Language.Telugu, LanguageChooser.Choose("en", Language.Telugu, seconds: 0.6, previous: Language.Telugu));
        Assert.Equal(Language.English, LanguageChooser.Choose("te", Language.Telugu, seconds: 0.6, previous: Language.English));
    }

    [Fact]
    public void VeryShortFirstSegment_IsJudgedOnItsOwn() =>
        Assert.Equal(Language.Telugu, LanguageChooser.Choose("te", Language.Telugu, seconds: 0.6, previous: null));
}

public class SpeechRouterTests
{
    private sealed class TextAsr(Func<string> text) : IAsr
    {
        public int Calls;
        public string Transcribe(float[] pcm) { Calls++; return text(); }
        public void Dispose() { }
    }

    private sealed class FixedLid(params string[] codes) : ILanguageIdentifier
    {
        public int Calls;
        public string Identify(float[] pcm16k) => codes[Math.Min(Calls++, codes.Length - 1)];
    }

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16000)];

    // "Quarkus deployed the service": three of four words are English, so the English check alone cannot decide.
    private const string Uncertain = "Quarkus deployed the service";

    private static (SpeechRouter router, TextAsr english, TextAsr regional) Auto(string englishText, ILanguageIdentifier? lid = null,
        Func<IReadOnlySet<string>>? known = null)
    {
        var english = new TextAsr(() => englishText);
        var regional = new TextAsr(() => "నేను రేపు");
        var router = new SpeechRouter(l => l == Language.English ? english : regional, () => lid, SpeechMode.EnglishTelugu) { KnownTerms = known };
        return (router, english, regional);
    }

    [Fact]
    public void SingleMode_UsesItsOwnEngine_WithoutLanguageIdOrTheEnglishCheck()
    {
        var english = new TextAsr(() => "en-text");
        var telugu = new TextAsr(() => "te-text");
        var router = new SpeechRouter(l => l == Language.English ? english : telugu, () => null, SpeechMode.Telugu);
        Assert.Equal(new Segment("te-text", Language.Telugu), router.RecognizeSegment(Audio(2), previous: null));
        Assert.Equal(0, english.Calls);
    }

    [Fact]
    public void AutoMode_ClearEnglish_StaysEnglish_AndNeverNeedsLanguageId()
    {
        var lid = new FixedLid("te");
        var (router, _, regional) = Auto("Please review the pull request after standup.", lid);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(2), null)!.Language);
        Assert.Equal(0, lid.Calls);
        Assert.Equal(0, regional.Calls);
    }

    [Fact]
    public void AutoMode_TeluguHeardAsEnglishNonsense_SwitchesToTelugu_WithoutLanguageId()
    {
        // the real case: "Em chesthunnav ra" came out as "Aim Chesto naura"
        var lid = new FixedLid("en");
        var (router, _, regional) = Auto("Aim Chesto naura", lid);
        var segment = router.RecognizeSegment(Audio(2), null)!;
        Assert.Equal(Language.Telugu, segment.Language);
        Assert.Equal("నేను రేపు", segment.Text);
        Assert.Equal(1, regional.Calls);
        Assert.Equal(0, lid.Calls);
    }

    [Fact]
    public void AutoMode_EmptyEnglishResult_GoesToTelugu()
    {
        var (router, _, _) = Auto("  ");
        Assert.Equal(Language.Telugu, router.RecognizeSegment(Audio(2), null)!.Language);
    }

    [Fact]
    public void AutoMode_UncertainEnglish_IsDecidedByLanguageId()
    {
        var (toTelugu, _, _) = Auto(Uncertain, new FixedLid("te"));
        Assert.Equal(Language.Telugu, toTelugu.RecognizeSegment(Audio(3), null)!.Language);
        var (toEnglish, _, regional) = Auto(Uncertain, new FixedLid("en"));
        Assert.Equal(Language.English, toEnglish.RecognizeSegment(Audio(3), null)!.Language);
        Assert.Equal(0, regional.Calls);
    }

    [Fact]
    public void AutoMode_UncertainEnglish_WithoutLanguageId_StaysEnglish()
    {
        var (router, _, _) = Auto(Uncertain);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(3), null)!.Language);
    }

    [Fact]
    public void AutoMode_UserDictionaryTerms_CountAsEnglishWords()
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Quarkus", "Miyapur" };
        var (withTerms, _, _) = Auto("Quarkus Miyapur", known: () => terms);
        Assert.Equal(Language.English, withTerms.RecognizeSegment(Audio(2), null)!.Language);
        var (without, _, _) = Auto("Quarkus Miyapur");
        Assert.Equal(Language.Telugu, without.RecognizeSegment(Audio(2), null)!.Language);
    }

    [Fact]
    public void AutoMode_VeryShortUncertainSegment_FollowsThePreviousLanguage()
    {
        var (router, _, _) = Auto(Uncertain, new FixedLid("en"));
        Assert.Equal(Language.Telugu, router.RecognizeSegment(Audio(0.6), previous: Language.Telugu)!.Language);
    }

    [Fact]
    public void EmptyRecognitionResult_GivesNoSegment()
    {
        var router = new SpeechRouter(_ => new TextAsr(() => "  "), () => null, SpeechMode.English);
        Assert.Null(router.RecognizeSegment(Audio(2), null));
    }

    [Fact]
    public void ModeCanChangeAtRuntime()
    {
        var english = new TextAsr(() => "Hello there.");
        var hindi = new TextAsr(() => "नमस्ते");
        var router = new SpeechRouter(l => l == Language.English ? english : hindi, () => null, SpeechMode.English);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(2), null)!.Language);
        router.Mode = SpeechMode.Hindi;
        Assert.Equal(Language.Hindi, router.RecognizeSegment(Audio(2), null)!.Language);
    }
}
