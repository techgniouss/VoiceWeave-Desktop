using Microsoft.Win32;

namespace VoiceWeave.Win;

/// <summary>
/// The optional "Start with Windows" switch. It writes the standard per-user startup entry, so it appears under
/// Windows Settings > Apps > Startup (and Task Manager > Startup apps), where the user can also turn it off.
/// The installer turns it on for a fresh install; the user can turn it off here, in Windows Settings or in Task Manager.
/// </summary>
public sealed class StartupRegistration(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run", string valueName = "VoiceWeave")
{
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName) != null;
    }

    public void Set(bool enabled, string exePath)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(valueName, $"\"{exePath}\" --background");
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
