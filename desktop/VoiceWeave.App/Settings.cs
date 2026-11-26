using System.IO;
using System.Text.Json;

namespace VoiceWeave.App;

/// <summary>Per-user locations under %LOCALAPPDATA%\VoiceWeave.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceWeave");
    /// <summary>A models folder remembered in settings.json; the environment variable still wins when it is set.</summary>
    public static string? ModelsFolderSetting { get; set; }
    public static string Models =>
        Environment.GetEnvironmentVariable("VOICEWEAVE_MODELS") is { Length: > 0 } fromEnvironment ? fromEnvironment
        : ModelsFolderSetting is { Length: > 0 } fromSettings ? fromSettings
        : Path.Combine(Root, "models");
    public static string Dictionary => Path.Combine(Root, "dictionary.txt");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string History => Path.Combine(Root, "history.jsonl");
    public static string Log => Path.Combine(Root, "log.txt");
}

/// <summary>Small remembered preferences: whether the floating bar shows and where it sits.</summary>
public sealed class Settings
{
    public bool BarVisible { get; set; } = true;
    public double? BarLeft { get; set; }
    public double? BarTop { get; set; }

    /// <summary>Where the speech models live, if not the default folder under %LOCALAPPDATA%\VoiceWeave\models.</summary>
    public string? ModelsFolder { get; set; }

    /// <summary>Spoken language: en, te, hi or ta.</summary>
    public string Language { get; set; } = "en";

    /// <summary>True once the "still running in the tray" hint has been shown (it is shown only once).</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>True once the welcome page has been dismissed.</summary>
    public bool WelcomeDismissed { get; set; }

    /// <summary>Keep a list of past dictations (text only).</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>How many days history is kept; 0 means keep it until the user deletes it.</summary>
    public int HistoryDays { get; set; } = 30;

    /// <summary>Add a dictionary rule when the user corrects a word in the text box after a dictation.</summary>
    public bool LearnCorrections { get; set; } = true;

    /// <summary>Also fix words that sound like a dictionary term ("Sreekant" for Srikanth). It is a guess, so it can be turned off.</summary>
    public bool SoundAlikeMatching { get; set; } = true;

    /// <summary>Write Telugu, Hindi and Tamil in English letters ("nenu repu") instead of their own script.</summary>
    public bool RomanOutput { get; set; } = true;

    /// <summary>Look for a newer release on GitHub at start-up and every few hours. Nothing installs without a click.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Start a hands-free dictation when the wake phrase is heard. Off until the user turns it on (it needs a small extra model).</summary>
    public bool WakeWordEnabled { get; set; }

    /// <summary>What to say to start listening. Plain English words; the user can change it.</summary>
    public string WakeWordPhrase { get; set; } = "hey voice";

    /// <summary>How sure the model must be before it reacts (0 to 1). Raise it if it starts by mistake, lower it if it misses you.</summary>
    public float WakeWordThreshold { get; set; } = 0.25f;

    /// <summary>How strongly the phrase is favoured over other sounds. Higher finds it more easily and also fires more falsely.</summary>
    public float WakeWordBoost { get; set; } = 2.0f;

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
