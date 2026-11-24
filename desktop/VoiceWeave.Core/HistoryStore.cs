using System.Text.Json;

namespace VoiceWeave.Core;

/// <summary>One finished dictation. Only the text and a few facts about it are kept, never the audio.</summary>
public sealed record HistoryEntry(Guid Id, DateTimeOffset When, string Text, string Mode, double Seconds);

/// <summary>
/// The list of past dictations, newest first, saved as one JSON object per line so adding an entry is a cheap append.
/// Old entries fall away by count and, optionally, by age.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _path;
    private readonly int _maxEntries;
    private readonly TimeSpan? _retention;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly List<HistoryEntry> _entries = new(); // oldest first on disk, exposed newest first

    public HistoryStore(string path, int maxEntries = 1000, TimeSpan? retention = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _maxEntries = maxEntries;
        _retention = retention;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    /// <summary>Raised after any change, so a window showing the list can refresh.</summary>
    public event Action? Changed;

    public IReadOnlyList<HistoryEntry> All
    {
        get { lock (_gate) return _entries.OrderByDescending(e => e.When).ToList(); }
    }

    public IReadOnlyList<HistoryEntry> Search(string query)
    {
        var q = query.Trim();
        var all = All;
        return q.Length == 0 ? all : all.Where(e => e.Text.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void Add(HistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Text)) return;
        lock (_gate)
        {
            _entries.Add(entry);
            if (Prune()) Rewrite();
            else AppendLine(entry);
        }
        Changed?.Invoke();
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) == 0) return false;
            Rewrite();
        }
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            Rewrite();
        }
        Changed?.Invoke();
    }

    private void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return;
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line);
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Text)) _entries.Add(entry);
                }
                catch (JsonException) { /* a damaged line must not lose the rest of the history */ }
            }
            if (Prune()) Rewrite();
        }
    }

    /// <summary>Drops entries beyond the cap or older than the retention period; returns whether anything was dropped.</summary>
    private bool Prune()
    {
        var before = _entries.Count;
        if (_retention is { } keep)
        {
            var cutoff = _clock() - keep;
            _entries.RemoveAll(e => e.When < cutoff);
        }
        if (_entries.Count > _maxEntries)
        {
            var extra = _entries.Count - _maxEntries;
            var oldest = _entries.OrderBy(e => e.When).Take(extra).Select(e => e.Id).ToHashSet();
            _entries.RemoveAll(e => oldest.Contains(e.Id));
        }
        return _entries.Count != before;
    }

    private void AppendLine(HistoryEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n");
    }

    private void Rewrite()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllLines(temp, _entries.OrderBy(e => e.When).Select(e => JsonSerializer.Serialize(e)));
        File.Move(temp, _path, overwrite: true);
    }
}
