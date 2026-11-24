namespace VoiceWeave.Core;

public enum KeyEvent { HoldDown, HoldUp, EscDown, OtherKeyDown, RegionalDown }

public enum HotkeyAction { None, StartRecording, StopAndTranscribe, Cancel, Latch, ForceRegional }

/// <summary>Hold-to-talk, quick double-tap to latch hands-free, Esc to cancel, other keys cancel a hold.</summary>
public sealed class HotkeyMachine(long minHoldMs, long doubleTapMs)
{
    private enum State { Idle, Holding, HoldingSecondTap, Latched }

    private State _state = State.Idle;
    private long _downAt;
    private long _lastShortTapUp = -1;

    public HotkeyAction On(KeyEvent ev, long nowMs)
    {
        switch (_state)
        {
            case State.Idle:
                if (ev != KeyEvent.HoldDown) return HotkeyAction.None;
                _downAt = nowMs;
                var second = _lastShortTapUp >= 0 && nowMs - _lastShortTapUp <= doubleTapMs;
                _state = second ? State.HoldingSecondTap : State.Holding;
                return HotkeyAction.StartRecording;

            case State.Holding:
                switch (ev)
                {
                    case KeyEvent.HoldUp:
                        _state = State.Idle;
                        if (nowMs - _downAt < minHoldMs)
                        {
                            _lastShortTapUp = nowMs;
                            return HotkeyAction.Cancel;
                        }
                        return HotkeyAction.StopAndTranscribe;
                    case KeyEvent.EscDown:
                    case KeyEvent.OtherKeyDown:
                        _state = State.Idle;
                        _lastShortTapUp = -1;
                        return HotkeyAction.Cancel;
                    case KeyEvent.RegionalDown:
                        return HotkeyAction.ForceRegional;
                    default:
                        return HotkeyAction.None; // key auto-repeat
                }

            case State.HoldingSecondTap:
                switch (ev)
                {
                    case KeyEvent.HoldUp:
                        _lastShortTapUp = -1;
                        if (nowMs - _downAt < minHoldMs)
                        {
                            _state = State.Latched;
                            return HotkeyAction.Latch;
                        }
                        _state = State.Idle;
                        return HotkeyAction.StopAndTranscribe;
                    case KeyEvent.EscDown:
                    case KeyEvent.OtherKeyDown:
                        _state = State.Idle;
                        _lastShortTapUp = -1;
                        return HotkeyAction.Cancel;
                    case KeyEvent.RegionalDown:
                        return HotkeyAction.ForceRegional;
                    default:
                        return HotkeyAction.None;
                }

            case State.Latched:
                switch (ev)
                {
                    case KeyEvent.HoldDown:
                        _state = State.Idle;
                        return HotkeyAction.StopAndTranscribe;
                    case KeyEvent.EscDown:
                        _state = State.Idle;
                        return HotkeyAction.Cancel;
                    case KeyEvent.RegionalDown:
                        return HotkeyAction.ForceRegional;
                    default:
                        return HotkeyAction.None;
                }
        }
        return HotkeyAction.None;
    }
}
