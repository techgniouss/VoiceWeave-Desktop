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

    /// <summary>
    /// The line the keyword spotter reads: for each way of spelling the phrase in pieces, the pieces, then the boost and the
    /// threshold that trigger it. The model was trained on the splits a sentencepiece tokenizer picks, which is not always the
    /// fewest-pieces split, so every plausible split is listened for at once (separated by "/") and any of them can trigger it.
    /// </summary>
    public string? KeywordLine(string phrase, float boost, float threshold)
    {
        var words = Words(phrase);
        if (words == null) return null;
        var perWord = new List<List<string>>();
        foreach (var word in words)
        {
            var splits = Splits(WordStart + word);
            if (splits.Count == 0) return null;
            perWord.Add(splits.Take(MaxSplitsPerWord).Select(p => string.Join(' ', p)).ToList());
        }
        var variants = new List<string> { "" };
        foreach (var options in perWord)
            variants = variants.SelectMany(v => options.Select(o => v.Length == 0 ? o : v + " " + o)).Take(MaxVariants).ToList();
        var name = string.Join('_', words);
        return string.Join('/', variants.Select(v => string.Create(CultureInfo.InvariantCulture, $"{v} :{boost:0.0#} #{threshold:0.0#} @{name}")));
    }

    private const int MaxSplitsPerWord = 6;
    private const int MaxVariants = 32;

    /// <summary>Every way the vocabulary can spell the word, fewest pieces first.</summary>
    private List<List<string>> Splits(string word)
    {
        var result = new List<List<string>>();
        var current = new List<string>();
        void Walk(int at)
        {
            if (result.Count >= 200) return;
            if (at == word.Length) { result.Add(new List<string>(current)); return; }
            for (var end = at + 1; end <= Math.Min(word.Length, at + MaxPieceLength); end++)
            {
                var piece = word[at..end];
                if (!_pieces.Contains(piece)) continue;
                current.Add(piece);
                Walk(end);
                current.RemoveAt(current.Count - 1);
            }
        }
        Walk(0);
        return result.OrderBy(p => p.Count).ThenBy(p => string.Join(' ', p), StringComparer.Ordinal).ToList();
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
