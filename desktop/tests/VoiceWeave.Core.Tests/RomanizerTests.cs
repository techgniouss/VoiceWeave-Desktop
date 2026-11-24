namespace VoiceWeave.Core.Tests;

public class RomanStyleTests
{
    // Cases ported from the Android app's tests: ISO 15919 text in, the plain spelling people type out.
    [Theory]
    [InlineData("cālā", IndicScript.Telugu, "chaalaa")]
    [InlineData("chōṭā", IndicScript.Devanagari, "chhota")]
    [InlineData("calō", IndicScript.Devanagari, "chalo")]
    [InlineData("dhan'yavādālu", IndicScript.Telugu, "dhanyavaadaalu")]
    [InlineData("viśēṣa", IndicScript.Devanagari, "vishesh")]
    [InlineData("śrīkānt", IndicScript.Telugu, "shreekaant")]
    [InlineData("kr̥ṣṇa", IndicScript.Telugu, "krishna")]
    [InlineData("r̥ṣi", IndicScript.Devanagari, "rishi")]
    [InlineData("haiṁ", IndicScript.Devanagari, "hain")]
    [InlineData("hūm̐", IndicScript.Devanagari, "hoon")]
    [InlineData("namaskāraṁ", IndicScript.Telugu, "namaskaaram")]
    [InlineData("karēṅgē", IndicScript.Devanagari, "karenge")]
    [InlineData("jñāna", IndicScript.Devanagari, "gyaan")]
    [InlineData("koṭuṅkaḷ", IndicScript.Tamil, "kodungal")]
    [InlineData("eppaṭi", IndicScript.Tamil, "eppadi")]
    [InlineData("naṉṟi", IndicScript.Tamil, "nandri")]
    [InlineData("tamiḻ", IndicScript.Tamil, "thamizh")]
    [InlineData("cāppāṭu", IndicScript.Tamil, "saappaadu")]
    [InlineData("vaṇakkam", IndicScript.Tamil, "vanakkam")]
    [InlineData("ceṉṉai", IndicScript.Tamil, "chennai")]
    [InlineData("ceṉṉaiyil", IndicScript.Tamil, "chennaiyil")]
    [InlineData("ciṉṉa", IndicScript.Tamil, "chinna")]
    [InlineData("cīṉi", IndicScript.Tamil, "seeni")]
    [InlineData("cari", IndicScript.Tamil, "sari")]
    [InlineData("jā", IndicScript.Devanagari, "ja")]
    [InlineData("rahā", IndicScript.Devanagari, "raha")]
    [InlineData("acchā", IndicScript.Devanagari, "achchha")]
    [InlineData("chī", IndicScript.Devanagari, "chhi")]
    [InlineData("dillī", IndicScript.Devanagari, "dilli")]
    [InlineData("nāma", IndicScript.Devanagari, "naam")]
    [InlineData("ā", IndicScript.Devanagari, "aa")]
    [InlineData("ampu", IndicScript.Tamil, "ambu")]
    public void Word(string iso, IndicScript script, string expected) =>
        Assert.Equal(expected, RomanStyle.Word(iso, script));

    [Fact]
    public void SentenceCase_AfterFullStop() =>
        Assert.Equal("Naaku", RomanStyle.SentenceCase("nenu vellanu. ", "naaku"));

    [Theory]
    [InlineData("nenu vellanu", "naaku")]       // no sentence end
    [InlineData("nenu vellanu.", "naaku")]      // no space after the stop: still the same word run
    [InlineData("", "naaku")]                    // first word is left to the field's own text
    public void SentenceCase_LeftAloneOtherwise(string before, string word) =>
        Assert.Equal(word, RomanStyle.SentenceCase(before, word));
}

public class RomanizerTests
{
    [Theory]
    [InlineData("నేను రేపు", "nenu repu")]
    [InlineData("నేను రేపు ఉదయం హైదరాబాద్ వెళ్తున్నాను.", "nenu repu udayam haidaraabaad veltunnaanu.")]
    [InlineData("దయచేసి ఈ సమావేశాన్ని మధ్యాహ్నం మూడు గంటలకు మార్చండి.", "dayachesi ee samaaveshaanni madhyaahnam moodu gantalaku maarchandi.")]
    [InlineData("మీ పేరు ఏమిటి?", "mee peru emiti?")]
    public void Telugu(string native, string expected) => Assert.Equal(expected, Romanizer.Convert(native));

    [Theory]
    [InlineData("मुझे कल सुबह दस बजे बैठक में जाना है।", "mujhe kal subah das baje baithak men jaana hai.")]
    [InlineData("कृपया यह रिपोर्ट आज शाम तक भेज दीजिए।", "kripaya yah riport aaj shaam tak bhej deejie.")]
    public void Hindi(string native, string expected) => Assert.Equal(expected, Romanizer.Convert(native));

    [Theory]
    [InlineData("நான் நாளை காலை சென்னைக்கு செல்கிறேன்.", "naan naalai kaalai chennaikku selgiren.")]
    [InlineData("நன்றி", "nandri")]
    [InlineData("வணக்கம்", "vanakkam")]
    public void Tamil(string native, string expected) => Assert.Equal(expected, Romanizer.Convert(native));

    [Fact]
    public void LatinAndPunctuationAndSpacing_PassThroughUnchanged() =>
        Assert.Equal("Please send it to Srikanth, ok?", Romanizer.Convert("Please send it to Srikanth, ok?"));

    [Fact]
    public void MixedScriptsAndLatin_AreConvertedRunByRun() =>
        Assert.Equal("Please send nenu repu", Romanizer.Convert("Please send నేను రేపు"));

    [Fact]
    public void IndicDigits_BecomeAsciiDigits() =>
        Assert.Equal("naaku 3 undi", Romanizer.Convert("నాకు ౩ ఉంది"));

    [Fact]
    public void EmptyText_StaysEmpty() => Assert.Equal("", Romanizer.Convert(""));

    [Fact]
    public void SecondSentence_StartsWithACapital() =>
        Assert.Equal("nenu repu. Naaku telusu", Romanizer.Convert("నేను రేపు. నాకు తెలుసు"));
}
