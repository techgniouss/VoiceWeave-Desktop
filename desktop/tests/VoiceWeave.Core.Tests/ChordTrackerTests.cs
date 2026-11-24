namespace VoiceWeave.Core.Tests;

public class ChordTrackerTests
{
    private const uint LCtrl = 0xA2, RCtrl = 0xA3, LWin = 0x5B, RWin = 0x5C, Esc = 0x1B, KeyA = 0x41, Shift = 0xA0;

    [Fact]
    public void CtrlThenWin_StartsHold_ReleasingEitherEndsIt()
    {
        var c = new ChordTracker();
        Assert.Null(c.On(LCtrl, true));
        Assert.Equal(KeyEvent.HoldDown, c.On(LWin, true));
        Assert.Equal(KeyEvent.HoldUp, c.On(LWin, false));
        Assert.Null(c.On(LCtrl, false));
    }

    [Fact]
    public void WinThenCtrl_AlsoStartsHold_AndReleasingCtrlEndsIt()
    {
        var c = new ChordTracker();
        Assert.Null(c.On(RWin, true));
        Assert.Equal(KeyEvent.HoldDown, c.On(RCtrl, true));
        Assert.Equal(KeyEvent.HoldUp, c.On(RCtrl, false));
    }

    [Fact]
    public void KeyRepeat_DoesNotRestartTheHold()
    {
        var c = new ChordTracker();
        c.On(LCtrl, true);
        Assert.Equal(KeyEvent.HoldDown, c.On(LWin, true));
        Assert.Null(c.On(LWin, true));
        Assert.Null(c.On(LCtrl, true));
    }

    [Fact]
    public void LeftAndRightVariants_AreTrackedSeparately()
    {
        var c = new ChordTracker();
        c.On(LCtrl, true);
        c.On(RCtrl, true);
        c.On(LWin, true);                       // chord active
        Assert.Null(c.On(LCtrl, false));        // right Ctrl still held: chord continues
        Assert.Equal(KeyEvent.HoldUp, c.On(RCtrl, false));
    }

    [Fact]
    public void CtrlAlone_IsNeverAHold()
    {
        var c = new ChordTracker();
        Assert.Null(c.On(LCtrl, true));
        Assert.Equal(KeyEvent.OtherKeyDown, c.On(KeyA, true)); // Ctrl+A
        Assert.Null(c.On(LCtrl, false));
    }

    [Fact]
    public void OtherKeyDuringChord_ReportsOtherKeyDown_OnPressOnly()
    {
        var c = new ChordTracker();
        c.On(LCtrl, true);
        c.On(LWin, true);
        Assert.Equal(KeyEvent.OtherKeyDown, c.On(KeyA, true));
        Assert.Null(c.On(KeyA, false));
    }

    [Theory]
    [InlineData(0x10u)]   // Shift
    [InlineData(0xA0u)]   // left Shift
    [InlineData(0xA1u)]   // right Shift
    public void ShiftDuringChord_RequestsTheRegionalLanguage_WithoutCancelling(uint shift)
    {
        var c = new ChordTracker();
        c.On(LCtrl, true);
        c.On(LWin, true);
        Assert.Equal(KeyEvent.RegionalDown, c.On(shift, true));
        Assert.Null(c.On(shift, false));
        Assert.Equal(KeyEvent.HoldUp, c.On(LWin, false)); // the hold is still intact
    }

    [Fact]
    public void ShiftOutsideTheChord_IsJustAnotherKey()
    {
        var c = new ChordTracker();
        Assert.Equal(KeyEvent.OtherKeyDown, c.On(Shift, true));
        c.On(LCtrl, true);
        Assert.Equal(KeyEvent.OtherKeyDown, c.On(Shift, true)); // Ctrl+Shift alone is a normal shortcut
    }

    [Fact]
    public void Escape_ReportsEscDown()
    {
        var c = new ChordTracker();
        Assert.Equal(KeyEvent.EscDown, c.On(Esc, true));
        Assert.Null(c.On(Esc, false));
    }

    [Fact]
    public void ChordCanBeReleasedAndPressedAgain()
    {
        var c = new ChordTracker();
        c.On(LCtrl, true); c.On(LWin, true); c.On(LWin, false); c.On(LCtrl, false);
        c.On(LCtrl, true);
        Assert.Equal(KeyEvent.HoldDown, c.On(LWin, true));
    }

    [Theory]
    [InlineData(0x5Bu, true)]
    [InlineData(0x5Cu, true)]
    [InlineData(0xA2u, false)]
    [InlineData(0x41u, false)]
    public void IsWinKey(uint vk, bool expected) => Assert.Equal(expected, ChordTracker.IsWin(vk));
}
