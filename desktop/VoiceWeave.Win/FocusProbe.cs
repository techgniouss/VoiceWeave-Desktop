using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>Inspects the focused window/control: password field, elevated process, terminal, text before the cursor.</summary>
public static class FocusProbe
{
    private static readonly HashSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "wezterm-gui", "alacritty", "mintty", "ConEmu64", "ConEmu", "kitty", "Hyper",
    };

    public static FocusInfo Get()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new FocusInfo(false, false, false, "");
        GetWindowThreadProcessId(hwnd, out var pid);

        var terminal = false;
        try { terminal = Terminals.Contains(Process.GetProcessById((int)pid).ProcessName); } catch (ArgumentException) { }

        var elevatedTarget = !IsSelfElevated() && IsProcessElevatedOrInaccessible(pid);
        var (password, before) = ReadFocusedElement();
        return new FocusInfo(password, elevatedTarget, terminal, before);
    }

    private static (bool password, string before) ReadFocusedElement()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el == null) return (false, "");
            var password = (bool)el.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            var before = "";
            if (!password && el.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                var selection = ((TextPattern)pattern).GetSelection();
                if (selection.Length > 0)
                {
                    var range = ((TextPattern)pattern).DocumentRange.Clone();
                    range.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
                    before = range.GetText(200);
                    if (before.Length > 200) before = before[^200..];
                }
            }
            return (password, before);
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or COMException or Win32Exception or ArgumentException)
        {
            return (false, "");
        }
    }

    private static bool IsSelfElevated() => IsElevated(Process.GetCurrentProcess().Handle);

    private static bool IsProcessElevatedOrInaccessible(uint pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return true; // cannot even query it: assume we cannot type into it
        try { return IsElevated(h, throwOnDenied: false); }
        finally { CloseHandle(h); }
    }

    private static bool IsElevated(IntPtr process, bool throwOnDenied = true)
    {
        if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token)) return !throwOnDenied; // denied = higher integrity
        try
        {
            var size = Marshal.SizeOf<int>();
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, 20 /* TokenElevation */, buf, size, out _)) return !throwOnDenied;
                return Marshal.ReadInt32(buf) != 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { CloseHandle(token); }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int length, out int returned);
}
