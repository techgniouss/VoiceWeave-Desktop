using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceWeave.Win;

/// <summary>The name of the program whose window is in front (for example "slack"). Only the process name is read, never its contents.</summary>
public static class ForegroundApp
{
    /// <returns>The process name without ".exe", or null when there is no window or it cannot be read.</returns>
    public static string? ProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid == 0 ? null : Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
