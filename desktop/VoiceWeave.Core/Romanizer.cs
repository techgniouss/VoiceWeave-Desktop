using System.Text;

namespace VoiceWeave.Core;

/// <summary>
/// Writes Telugu, Hindi and Tamil text in English letters ("నేను రేపు" -> "nenu repu"): native script to ISO 15919 by the
/// shared layout of the Indic Unicode blocks, then <see cref="RomanStyle"/> turns it into everyday spelling. Anything
/// that is not one of those scripts passes through unchanged.
/// </summary>
public static class Romanizer
{
    private const int DevanagariBase = 0x0900, TamilBase = 0x0B80, TeluguBase = 0x0C00, BlockSize = 0x80;

    // Offsets inside an Indic block follow the same order in every script (the ISCII layout), so one table serves all three.
    private static readonly string[] IndependentVowels = new string[0x15]; // offsets 0x05..0x14
    private static readonly string[] Consonants = new string[0x3A];        // offsets 0x15..0x39
    private static readonly string[] VowelSigns = new string[0x4D];        // offsets 0x3E..0x4C

    static Romanizer()
    {
        // independent vowels
        Set(IndependentVowels, 0x05, "a", "ā", "i", "ī", "u", "ū", "r̥", "l̥", "e", "e", "ē", "ai", "o", "o", "ō", "au");
        // consonants 0x15..0x39
        Set(Consonants, 0x15,
            "k", "kh", "g", "gh", "ṅ", "c", "ch", "j", "jh", "ñ", "ṭ", "ṭh", "ḍ", "ḍh", "ṇ",
            "t", "th", "d", "dh", "n", "ṉ", "p", "ph", "b", "bh", "m", "y", "r", "ṟ", "l", "ḷ", "ḻ", "v", "ś", "ṣ", "s", "h");
        // dependent vowel signs 0x3E..0x4C
        Set(VowelSigns, 0x3E, "ā", "i", "ī", "u", "ū", "r̥", "r̥̄", "e", "e", "ē", "ai", "o", "o", "ō", "au");
    }

    private static void Set(string[] table, int start, params string[] values)
    {
        for (var i = 0; i < values.Length; i++) table[start + i] = values[i];
    }

    public static string Convert(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var output = new StringBuilder(text.Length + 8);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] is '।' or '॥') { output.Append('.'); i++; continue; } // danda: the Hindi full stop
            var script = ScriptOf(text[i]);
            if (script == null) { output.Append(text[i]); i++; continue; }
            var end = i;
            while (end < text.Length && text[end] is not ( '।' or '॥') && (ScriptOf(text[end]) == script || IsJoiner(text[end]))) end++;
            var (baseCode, kind) = script.Value;
            var iso = ToIso(text.AsSpan(i, end - i), baseCode);
            // Digits of the script come out as ASCII digits; words as plain spelling with sentence capitals.
            foreach (var piece in SplitDigits(iso))
            {
                if (piece.isDigit) output.Append(piece.text);
                else output.Append(RomanStyle.SentenceCase(output.ToString(), RomanStyle.Word(piece.text, kind)));
            }
            i = end;
        }
        return output.ToString();
    }

    private static bool IsJoiner(char c) => c is '‌' or '‍';

    private static (int baseCode, IndicScript kind)? ScriptOf(char c) => c switch
    {
        >= (char)DevanagariBase and < (char)(DevanagariBase + BlockSize) => (DevanagariBase, IndicScript.Devanagari),
        >= (char)TamilBase and < (char)(TamilBase + BlockSize) => (TamilBase, IndicScript.Tamil),
        >= (char)TeluguBase and < (char)(TeluguBase + BlockSize) => (TeluguBase, IndicScript.Telugu),
        _ => null,
    };

    private static string ToIso(ReadOnlySpan<char> run, int baseCode)
    {
        var sb = new StringBuilder(run.Length * 2);
        for (var i = 0; i < run.Length; i++)
        {
            var c = run[i];
            if (IsJoiner(c)) continue;
            var off = c - baseCode;
            if (off is >= 0x66 and <= 0x6F)
            {
                sb.Append((char)('0' + (off - 0x66))); // script digit
                continue;
            }
            if (off is >= 0x15 and <= 0x39 && Consonants[off] != null)
            {
                sb.Append(Consonants[off]);
                // Devanagari nukta after a consonant only modifies its sound
                if (i + 1 < run.Length && run[i + 1] - baseCode == 0x3C) i++;
                if (i + 1 < run.Length)
                {
                    var next = run[i + 1] - baseCode;
                    if (next is >= 0x3E and <= 0x4C && VowelSigns[next] != null) { sb.Append(VowelSigns[next]); i++; continue; }
                    if (next == 0x4D) { i++; continue; } // virama: no vowel
                }
                sb.Append('a'); // inherent vowel
                continue;
            }
            switch (off)
            {
                case 0x00 or 0x01: sb.Append("m̐"); continue;   // candrabindu
                case 0x02: sb.Append('ṁ'); continue;                  // anusvara
                case 0x03: sb.Append('ḥ'); continue;                  // visarga
            }
            if (off is >= 0x05 and <= 0x14 && IndependentVowels[off] != null) { sb.Append(IndependentVowels[off]); continue; }
            // length marks, nukta, avagraha, om and anything else: skipped
        }
        return sb.ToString();
    }

    private static IEnumerable<(string text, bool isDigit)> SplitDigits(string iso)
    {
        var start = 0;
        for (var i = 0; i < iso.Length; i++)
        {
            if (!char.IsAsciiDigit(iso[i])) continue;
            if (i > start) yield return (iso[start..i], false);
            var d = i;
            while (d < iso.Length && char.IsAsciiDigit(iso[d])) d++;
            yield return (iso[i..d], true);
            i = d - 1;
            start = d;
        }
        if (start < iso.Length) yield return (iso[start..], false);
    }
}
