namespace VoiceWeave.Engine;

/// <summary>Picks how casual the cleaned-up text should be from the program the person is typing into (chat is relaxed, mail is formal).</summary>
public static class ToneByApp
{
    private static readonly HashSet<string> Chat = new(StringComparer.OrdinalIgnoreCase)
    {
        "slack", "ms-teams", "teams", "discord", "whatsapp", "whatsapp.root", "telegram", "signal", "messenger", "skype", "viber", "line", "wechat",
    };

    private static readonly HashSet<string> Formal = new(StringComparer.OrdinalIgnoreCase)
    {
        "outlook", "olk", "hxoutlook", "winword", "thunderbird", "wordpad", "onenote", "powerpnt", "acrord32", "soffice",
    };

    /// <param name="processName">The program's process name without ".exe" (for example "slack"), or null when it is not known.</param>
    public static Tone For(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return Tone.Casual;
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (Chat.Contains(name)) return Tone.VeryCasual;
        if (Formal.Contains(name)) return Tone.Formal;
        return Tone.Casual;
    }
}
