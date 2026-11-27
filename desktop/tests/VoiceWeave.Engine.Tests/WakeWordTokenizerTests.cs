using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

public class WakeWordTokenizerTests
{
    private const string S = "▁";

    // A tiny vocabulary shaped like the model's: word-start pieces, word pieces and single letters.
    private static WakeWordTokenizer Make() => new(new[]
    {
        S + "HE", "Y", S + "VOICE", S + "VO", "ICE", S + "H", "E", S + "V", "O", "I", "C", S + "HEY", S + "WEAVE", "W", "A", "V",
    });

    [Fact]
    public void PrefersTheFewestPieces()
    {
        // "▁HEY" exists as one piece, so it beats "▁HE" + "Y"
        Assert.Equal($"{S}HEY {S}VOICE", Make().Encode("hey voice"));
    }

    [Fact]
    public void FallsBackToShorterPieces()
    {
        var tokenizer = new WakeWordTokenizer(new[] { S + "HE", "Y", S + "VO", "ICE" });
        Assert.Equal($"{S}HE Y {S}VO ICE", tokenizer.Encode("Hey Voice"));
    }

    [Fact]
    public void IgnoresCaseAndExtraSpaces() => Assert.Equal(Make().Encode("hey voice"), Make().Encode("  HEY   Voice "));

    [Fact]
    public void ALetterTheModelCannotSpell_GivesNull() => Assert.Null(Make().Encode("hey zebra"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hey 123")]
    [InlineData("hey, voice")]
    [InlineData("a b c d e f g")]
    public void NotPlainEnglishWords_GivesNull(string phrase) => Assert.Null(Make().Encode(phrase));

    [Fact]
    public void KeywordLine_CarriesBoostThresholdAndName()
    {
        var line = Make().KeywordLine("hey voice", 2.0f, 0.25f);
        Assert.Equal($"{S}HEY {S}VOICE :2.0 #0.25 @HEY_VOICE", line!.Split('/')[0]);
    }

    [Fact]
    public void KeywordLine_ListensForEverySplit_FewestPiecesFirst()
    {
        var tokenizer = new WakeWordTokenizer(new[] { S.ToString(), "H", "E", "Y", "HE", "HEY", S + "HE", S + "HEY" });
        var line = tokenizer.KeywordLine("hey", 2.0f, 0.25f)!;
        var variants = line.Split('/');
        Assert.True(variants.Length > 1);
        Assert.StartsWith($"{S}HEY :2.0", variants[0]);
        Assert.All(variants, v => Assert.EndsWith("#0.25 @HEY", v));
    }

    [Fact]
    public void KeywordLine_UsesADotWhateverTheComputerLanguage()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains(":2.5 #0.3", Make().KeywordLine("hey voice", 2.5f, 0.3f));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void FromTokensFile_ReadsTheFirstColumn()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"<blk> 0\n{S}HEY 1\n{S}VOICE 2\n");
            Assert.Equal($"{S}HEY {S}VOICE", WakeWordTokenizer.FromTokensFile(path).Encode("hey voice"));
        }
        finally { File.Delete(path); }
    }
}
