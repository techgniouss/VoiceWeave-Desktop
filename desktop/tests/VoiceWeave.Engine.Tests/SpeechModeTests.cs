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
    private sealed class NamedAsr(string name) : IAsr
    {
        public int Calls;
        public string Transcribe(float[] pcm) { Calls++; return name; }
        public void Dispose() { }
    }

    private sealed class FixedLid(params string[] codes) : ILanguageIdentifier
    {
        private int _i;
        public string Identify(float[] pcm16k) => codes[Math.Min(_i++, codes.Length - 1)];
    }

    private static float[] Audio(double seconds) => new float[(int)(seconds * 16000)];

    [Fact]
    public void SingleMode_UsesItsOwnEngine_WithoutLanguageId()
    {
        var engines = new Dictionary<Language, NamedAsr> { [Language.Telugu] = new("te-text"), [Language.English] = new("en-text") };
        var router = new SpeechRouter(l => engines[l], () => null, SpeechMode.Telugu);
        var seg = router.RecognizeSegment(Audio(2), previous: null);
        Assert.Equal(new Segment("te-text", Language.Telugu), seg);
        Assert.Equal(0, engines[Language.English].Calls);
    }

    [Fact]
    public void AutoMode_RoutesEachSegmentByItsLanguage()
    {
        var engines = new Dictionary<Language, NamedAsr> { [Language.Telugu] = new("te-text"), [Language.English] = new("en-text") };
        var lid = new FixedLid("en", "te");
        var router = new SpeechRouter(l => engines[l], () => lid, SpeechMode.EnglishTelugu);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(2), null)!.Language);
        Assert.Equal(Language.Telugu, router.RecognizeSegment(Audio(2), Language.English)!.Language);
    }

    [Fact]
    public void AutoMode_WithoutALanguageIdYet_FallsBackToEnglish()
    {
        var engines = new Dictionary<Language, NamedAsr> { [Language.Telugu] = new("te-text"), [Language.English] = new("en-text") };
        var router = new SpeechRouter(l => engines[l], () => null, SpeechMode.EnglishTelugu);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(2), null)!.Language);
    }

    [Fact]
    public void EmptyRecognitionResult_GivesNoSegment()
    {
        var router = new SpeechRouter(_ => new NamedAsr("  "), () => null, SpeechMode.English);
        Assert.Null(router.RecognizeSegment(Audio(2), null));
    }

    [Fact]
    public void ModeCanChangeAtRuntime()
    {
        var engines = new Dictionary<Language, NamedAsr> { [Language.Hindi] = new("hi-text"), [Language.English] = new("en-text") };
        var router = new SpeechRouter(l => engines[l], () => null, SpeechMode.English);
        Assert.Equal(Language.English, router.RecognizeSegment(Audio(2), null)!.Language);
        router.Mode = SpeechMode.Hindi;
        Assert.Equal(Language.Hindi, router.RecognizeSegment(Audio(2), null)!.Language);
    }
}
