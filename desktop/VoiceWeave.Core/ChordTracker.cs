namespace VoiceWeave.Core;

/// <summary>
/// Turns raw key transitions into hotkey events for the Ctrl+Win chord: holding both starts a dictation,
/// releasing either ends it. Any other key pressed reports <see cref="KeyEvent.OtherKeyDown"/> so shortcuts such as
/// Ctrl+C or Win+Ctrl+D cancel a recording instead of being mistaken for dictation.
/// </summary>
public sealed class ChordTracker
{
    private const uint VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_ESCAPE = 0x1B;

    private bool _ctrlLeft, _ctrlRight, _winLeft, _winRight, _active;

    public static bool IsWin(uint vk) => vk is VK_LWIN or VK_RWIN;

    public KeyEvent? On(uint vk, bool down)
    {
        switch (vk)
        {
            case VK_LCONTROL or VK_CONTROL: _ctrlLeft = down; return Chord();
            case VK_RCONTROL: _ctrlRight = down; return Chord();
            case VK_LWIN: _winLeft = down; return Chord();
            case VK_RWIN: _winRight = down; return Chord();
        }
        if (!down) return null;
        if (vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT && _active) return KeyEvent.RegionalDown; // Ctrl+Win+Shift: use the regional language
        return vk == VK_ESCAPE ? KeyEvent.EscDown : KeyEvent.OtherKeyDown;
    }

    private KeyEvent? Chord()
    {
        var now = (_ctrlLeft || _ctrlRight) && (_winLeft || _winRight);
        if (now == _active) return null; // key repeat, or a second key of the same kind
        _active = now;
        return now ? KeyEvent.HoldDown : KeyEvent.HoldUp;
    }
}
