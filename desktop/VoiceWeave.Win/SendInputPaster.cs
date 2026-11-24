using System.Runtime.InteropServices;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>Sends Ctrl+V (Ctrl+Shift+V for terminals) to the focused window.</summary>
public sealed class SendInputPaster : IPaster
{
    private const ushort VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;

    public void CtrlV(bool terminal)
    {
        var keys = new List<INPUT> { Key(VK_CONTROL, false) };
        if (terminal) keys.Add(Key(VK_SHIFT, false));
        keys.Add(Key(VK_V, false));
        keys.Add(Key(VK_V, true));
        if (terminal) keys.Add(Key(VK_SHIFT, true));
        keys.Add(Key(VK_CONTROL, true));
        var sent = SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != keys.Count) throw new InvalidOperationException("Windows rejected the paste keystrokes (the target window may be elevated).");
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;   // largest member; keeps sizeof(INPUT) correct
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
