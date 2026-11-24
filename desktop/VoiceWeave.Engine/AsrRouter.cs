namespace VoiceWeave.Engine;

/// <summary>Sends each recording to the engine of the currently selected language.</summary>
public sealed class AsrRouter(Func<Language, IAsr> resolve, Language initial) : IAsr
{
    private volatile int _current = (int)initial;
    private volatile int _last = (int)initial;

    public Language Current
    {
        get => (Language)_current;
        set => _current = (int)value;
    }

    /// <summary>The language used by the most recent transcription (what the text after it was written in).</summary>
    public Language LastLanguage => (Language)_last;

    public string Transcribe(float[] pcm16k)
    {
        var language = Current;
        _last = (int)language;
        return resolve(language).Transcribe(pcm16k);
    }

    public void Dispose() { }
}
