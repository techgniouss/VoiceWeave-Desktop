using System.Globalization;

namespace VoiceWeave.Engine;

/// <summary>
/// Turns a phrase the user typed ("hey voice") into the word pieces the keyword-spotting model understands, so any phrase can
/// be a wake word without training anything. The model's own vocabulary (its tokens.txt) is all that is needed.
/// </summary>
public sealed class WakeWordTokenizer
{
    private const char WordStart = '▁'; // "▁", the mark sentencepiece puts in front of a word
    private const int MaxPieceLength = 16;

    private readonly HashSet<string> _pieces;

    public WakeWordTokenizer(IEnumerable<string> vocabulary) => _pieces = new HashSet<string>(vocabulary, StringComparer.Ordinal);

    /// <summary>Reads the first column of a sherpa-onnx tokens.txt ("piece id" per line).</summary>
    public static WakeWordTokenizer FromTokensFile(string path) =>
        new(File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => { var space = line.LastIndexOf(' '); return space > 0 ? line[..space] : line; }));

    /// <summary>The words of a phrase as the model hears them: upper case letters and apostrophes only, at most 6 words.</summary>
    public static string[]? Words(string phrase)
    {
        var words = phrase.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToUpperInvariant())
            .ToArray();
        if (words.Length == 0 || words.Length > 6) return null;
        return words.All(w => w.Length > 0 && w.All(c => c is >= 'A' and <= 'Z' or '\'')) ? words : null;
    }

    /// <summary>The pieces for <paramref name="phrase"/>, space separated, or null when the phrase has a letter the model has no piece for.</summary>
    public string? Encode(string phrase)
    {
        var words = Words(phrase);
        if (words == null) return null;
        var all = new List<string>();
        foreach (var word in words)
        {
            var pieces = Split(WordStart + word);
            if (pieces == null) return null;
            all.AddRange(pieces);
        }
        return string.Join(' ', all);
    }

    /// <summary>The line the keyword spotter reads: pieces, then the boost and the threshold that trigger it.</summary>
    public string? KeywordLine(string phrase, float boost, float threshold)
    {
        var pieces = Encode(phrase);
        if (pieces == null) return null;
        var name = string.Join('_', Words(phrase)!);
        return string.Create(CultureInfo.InvariantCulture, $"{pieces} :{boost:0.0#} #{threshold:0.0#} @{name}");
    }

    // Fewest pieces wins (long pieces are what the model saw most in training); a word the vocabulary cannot spell gives null.
    private List<string>? Split(string word)
    {
        var best = new int[word.Length + 1];
        var from = new int[word.Length + 1];
        Array.Fill(best, int.MaxValue);
        best[0] = 0;
        for (var end = 1; end <= word.Length; end++)
            for (var start = Math.Max(0, end - MaxPieceLength); start < end; start++)
            {
                if (best[start] == int.MaxValue || !_pieces.Contains(word[start..end])) continue;
                if (best[start] + 1 < best[end]) { best[end] = best[start] + 1; from[end] = start; }
            }
        if (best[word.Length] == int.MaxValue) return null;
        var result = new List<string>();
        for (var at = word.Length; at > 0; at = from[at]) result.Add(word[from[at]..at]);
        result.Reverse();
        return result;
    }
}
