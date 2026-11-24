using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VoiceWeave.Core;

public enum IndicScript { Devanagari, Telugu, Tamil }

/// <summary>
/// Turns one word of ISO 15919 Latin into the plain spelling people type when they write an Indian language in English
/// letters: chalo, shaadi, krishna, nandri, kodungal. Ported from the Android app so both apps spell alike.
/// </summary>
public static class RomanStyle
{
    private const char AspiratedCh = ''; // everyday "chh"
    private const char PlainCh = '';     // everyday "ch"
    private const string Vowels = "aāiīuūeēoō";
    private const string Sonorants = "rṟlḷḻyvmnṇṉñṅ";

    private static readonly Regex Marks = new(@"\p{M}+", RegexOptions.Compiled);

    public static string Word(string iso, IndicScript script)
    {
        // Separators between n+y or a+e (written as an apostrophe) are not something people type.
        var s = iso.Normalize(NormalizationForm.FormC).Replace("'", "").Replace("’", "");
        if (script == IndicScript.Devanagari) s = Schwa.Apply(s);
        if (script == IndicScript.Tamil) s = Tamil(s);
        if (script == IndicScript.Devanagari) s = s.Replace("jñ", "gy");
        s = s.Replace("m̐", "n");

        // An anusvara is "m" before p/b/m, "n" before other consonants, and at the end of a word depends on the language
        // (Hindi haiṁ -> hain, Telugu namaskāraṁ -> namaskaaram).
        var finalAnusvara = script == IndicScript.Devanagari ? "n" : "m";
        s = Regex.Replace(s, "[ṁṃ](?=ph|bh|[pbm])", "m");
        s = Regex.Replace(s, "[ṁṃ](?=\\p{L})", "n");
        s = s.Replace("ṁ", finalAnusvara).Replace("ṃ", finalAnusvara);

        s = Regex.Replace(s, "ñ(?=[cj])", "n").Replace("ñ", "ny");
        s = Regex.Replace(s, "ṅ(?=[kg])", "n").Replace("ṅ", "ng");
        s = s.Replace("ś", "sh").Replace("ṣ", "sh").Replace("ḻ", "zh");
        s = s.Replace("r̥̄", "ree").Replace("r̥", "ri").Replace("l̥", "li");
        s = s.Replace("ch", AspiratedCh.ToString()).Replace("c", PlainCh.ToString());
        s = s.Replace("ā", "aa").Replace("ī", "ee").Replace("ū", "oo").Replace("‌", "").Replace("‍", "");

        var plain = Marks.Replace(s.Normalize(NormalizationForm.FormD), "");
        // Hindi speakers write jaa as ja and dillee as dilli: the long vowel only shows inside a word.
        if (script == IndicScript.Devanagari)
            plain = Regex.Replace(Regex.Replace(plain, "(?<=[a-z])aa$", "a"), "(?<=[a-z])ee$", "i");
        return plain.Replace(AspiratedCh.ToString(), "chh").Replace(PlainCh.ToString(), "ch");
    }

    /// <summary>
    /// Native scripts have no case, so romanized text would stay lower case. Capitalises the word when the text before it
    /// ended a sentence. The first word is left alone because the field's own text decides its case.
    /// </summary>
    public static string SentenceCase(string before, string word)
    {
        var end = before.Length;
        while (end > 0 && char.IsWhiteSpace(before[end - 1])) end--;
        if (end == 0 || end == before.Length || ".?!".IndexOf(before[end - 1]) < 0 || word.Length == 0) return word;
        return char.ToUpperInvariant(word[0]) + word[1..];
    }

    /// <summary>
    /// Tamil has one letter for each stop and says it voiced between vowels and after nasals, so kal after a vowel is
    /// spoken gal and written that way: neengal, eppadi, nandri. ISO spelling alone gives neengkal, eppati, nanri.
    /// </summary>
    private static string Tamil(string s)
    {
        s = s.Replace("ṉṟ", "ndr").Replace("ṟṟ", "ttr");
        var output = new StringBuilder(s.Length + 4);
        // ச starts most words as s (sari, sollu) but a few common names keep ch: Chennai, China (but seeni is sugar).
        var first = s.StartsWith("ceṉṉ", StringComparison.Ordinal) || s.StartsWith("ciṉṉ", StringComparison.Ordinal) || s.StartsWith("cīṉā", StringComparison.Ordinal) ? 1 : 0;
        if (first == 1) output.Append(PlainCh);
        for (var i = first; i < s.Length; i++)
        {
            var c = s[i];
            if ("kcṭtp".IndexOf(c) < 0) { output.Append(c); continue; }
            if (i + 1 < s.Length && s[i + 1] == c)
            {
                output.Append(c switch { 'k' => "kk", 'c' => PlainCh.ToString(), 'ṭ' => "tt", 't' => "th", _ => "pp" });
                i++;
                continue;
            }
            var previous = i > 0 ? s[i - 1] : '\0';
            var voiced = i > 0 && (Vowels.IndexOf(previous) >= 0 || Sonorants.IndexOf(previous) >= 0);
            output.Append(c switch
            {
                'k' => voiced ? "g" : "k",
                'c' => voiced && previous == 'ñ' ? "j" : "s",
                'ṭ' => voiced ? "d" : "t",
                't' => voiced ? "dh" : "th",
                _ => voiced ? "b" : "p",
            });
        }
        return output.ToString();
    }
}

/// <summary>
/// Hindi drops the inherent "a" at the end of a word and in many middle syllables, but a plain transliteration keeps it.
/// Works on one word of ISO-style Latin and removes the silent vowels: kamala -> kamal, aapakaa -> aapkaa.
/// </summary>
internal static class Schwa
{
    private const string Vowels = "aāiīuūeēoō";
    private const string Aspirable = "kgcjṭḍtdpb";

    public static string Apply(string word)
    {
        var units = new List<string>();
        foreach (var c in word)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark && units.Count > 0)
                units[^1] += c;
            else if (c == 'h' && units.Count > 0 && units[^1].Length == 1 && Aspirable.IndexOf(units[^1][0]) >= 0)
                units[^1] += c;
            else
                units.Add(c.ToString());
        }
        if (units.Count(IsVowel) < 2) return word;
        var last = units.Count - 1;
        if (units[last] == "a" && last > 0 && IsConsonant(units[last - 1])) units.RemoveAt(last);
        for (var i = units.Count - 1; i >= 2; i--)
        {
            if (units[i] == "a" && IsConsonant(units[i - 1]) && IsVowel(units[i - 2])
                && i + 2 < units.Count && IsConsonant(units[i + 1]) && IsVowel(units[i + 2]))
                units.RemoveAt(i);
        }
        return string.Concat(units);
    }

    private static bool IsVowel(string unit) => unit.Length == 1 && Vowels.IndexOf(unit[0]) >= 0;
    private static bool IsConsonant(string unit) => char.IsLetter(unit[0]) && !IsVowel(unit);
}
