using System.Diagnostics;
using System.Runtime.InteropServices;
using VoiceWeave.Core;

namespace VoiceWeave.Win;

/// <summary>
/// System-wide low-level keyboard hook for the Ctrl+Win chord. It only observes: every key is passed on unchanged, and
/// keystrokes injected by software (such as our own paste) are ignored. Handlers run on the hook thread and must return quickly.
/// When a dictation chord ends with the Windows key, a harmless unassigned key is injected first so Windows does not
/// open the Start menu.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10, WM_QUIT = 0x0012;

    private readonly ChordTracker _chord = new();
    private readonly LowLevelProc _proc; // held so the delegate is not collected while hooked
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hook;
    private uint _threadId;

    public event Action<VoiceWeave.Core.KeyEvent, long>? KeyEvent;

    public KeyboardHook()
    {
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "VoiceWeave keyboard hook" };
        _thread.Start();
        _ready.Wait();
        if (_hook == IntPtr.Zero) throw new InvalidOperationException("Could not install the keyboard hook.");
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        _ready.Set();
        if (_hook == IntPtr.Zero) return;
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((info.flags & LLKHF_INJECTED) == 0)
            {
                var w = (int)wParam;
                var down = w is WM_KEYDOWN or WM_SYSKEYDOWN;
                var up = w is WM_KEYUP or WM_SYSKEYUP;
                if (down || up)
                {
                    var ev = _chord.On(info.vkCode, down);
                    if (ev is { } e)
                    {
                        if (e == Core.KeyEvent.HoldUp && ChordTracker.IsWin(info.vkCode)) SuppressStartMenu();
                        KeyEvent?.Invoke(e, Environment.TickCount64);
                    }
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam); // never swallow a key
    }

    private static void SuppressStartMenu()
    {
        const ushort VK_UNASSIGNED = 0xE8;
        var inputs = new INPUT[]
        {
            new() { type = 1, ki = new KEYBDINPUT { wVk = VK_UNASSIGNED } },
            new() { type = 1, ki = new KEYBDINPUT { wVk = VK_UNASSIGNED, dwFlags = 0x0002 } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    private delegate IntPtr LowLevelProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
}
