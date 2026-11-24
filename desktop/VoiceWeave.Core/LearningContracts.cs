namespace VoiceWeave.Core;

/// <summary>The text of the field a dictation was pasted into, as it reads right now.</summary>
public interface IFieldText
{
    /// <returns>The whole text, or null when the field cannot be read (a password box, a terminal, a closed window).</returns>
    string? Read();
}

/// <summary>Where learned rules are kept (the personal dictionary file).</summary>
public interface IRuleStore
{
    string Read();
    void Append(string line);
    bool Remove(string line);
}
