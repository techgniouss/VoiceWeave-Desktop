using System.IO;
using VoiceWeave.Core;

namespace VoiceWeave.App;

/// <summary>The personal dictionary file as a place to add and remove learned rules.</summary>
sealed class DictionaryFileStore : IRuleStore
{
    private readonly object _gate = new();

    public string Read()
    {
        lock (_gate)
        {
            try { return File.ReadAllText(AppPaths.Dictionary); }
            catch (FileNotFoundException) { return ""; }
        }
    }

    public void Append(string line)
    {
        lock (_gate)
        {
            var text = Read();
            if (text.Length > 0 && !text.EndsWith('\n')) text += "\n";
            File.WriteAllText(AppPaths.Dictionary, text + line + "\n"); // the dictionary watcher reloads it
        }
    }

    public bool Remove(string line)
    {
        lock (_gate)
        {
            var text = Read();
            var updated = DictionaryRules.RemoveLines(text, new[] { line });
            if (updated == text) return false;
            File.WriteAllText(AppPaths.Dictionary, updated);
            return true;
        }
    }
}
