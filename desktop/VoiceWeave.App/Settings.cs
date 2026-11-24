using System.IO;
using System.Text.Json;

namespace VoiceWeave.App;

/// <summary>Per-user locations under %LOCALAPPDATA%\VoiceWeave.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceWeave");
    public static string Models => Environment.GetEnvironmentVariable("VOICEWEAVE_MODELS") is { Length: > 0 } custom ? custom : Path.Combine(Root, "models");
    public static string Dictionary => Path.Combine(Root, "dictionary.txt");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
}

/// <summary>Small remembered preferences: whether the floating bar shows and where it sits.</summary>
public sealed class Settings
{
    public bool BarVisible { get; set; } = true;
    public double? BarLeft { get; set; }
    public double? BarTop { get; set; }

    /// <summary>Spoken language: en, te, hi or ta.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Write Telugu, Hindi and Tamil in English letters ("nenu repu") instead of their own script.</summary>
    public bool RomanOutput { get; set; } = true;

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a lost position is not worth a crash */ }
    }
}
