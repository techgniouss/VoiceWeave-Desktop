namespace VoiceWeave.Core.Tests;

public class HotkeyMachineTests
{
    private static HotkeyMachine New() => new(minHoldMs: 200, doubleTapMs: 400);

    [Fact]
    public void HoldThenRelease_Transcribes()
    {
        var m = New();
        Assert.Equal(HotkeyAction.StartRecording, m.On(KeyEvent.HoldDown, 0));
        Assert.Equal(HotkeyAction.StopAndTranscribe, m.On(KeyEvent.HoldUp, 1000));
    }

    [Fact]
    public void LearnKeyDuringTheChord_LearnsInsteadOfDictating()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        Assert.Equal(HotkeyAction.Learn, m.On(KeyEvent.LearnDown, 50));
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.HoldUp, 400)); // releasing the chord afterwards does nothing
    }

    [Fact]
    public void TapUnderMinHold_CancelsSilently()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        Assert.Equal(HotkeyAction.Cancel, m.On(KeyEvent.HoldUp, 100));
    }

    [Fact]
    public void DoubleTap_LatchesHandsFree_NextTapStops()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        m.On(KeyEvent.HoldUp, 100);              // short tap
        Assert.Equal(HotkeyAction.StartRecording, m.On(KeyEvent.HoldDown, 250)); // second tap within window
        Assert.Equal(HotkeyAction.Latch, m.On(KeyEvent.HoldUp, 330));
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.HoldUp, 400));
        Assert.Equal(HotkeyAction.StopAndTranscribe, m.On(KeyEvent.HoldDown, 5000));
    }

    [Fact]
    public void Esc_CancelsWhileRecording()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        Assert.Equal(HotkeyAction.Cancel, m.On(KeyEvent.EscDown, 500));
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.HoldUp, 600));
    }

    [Fact]
    public void OtherKeyDuringHold_Cancels_SoShortcutsStillWork()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        Assert.Equal(HotkeyAction.Cancel, m.On(KeyEvent.OtherKeyDown, 50));
    }

    [Fact]
    public void RegionalDuringAHold_ForcesTheRegionalLanguage_AndKeepsRecording()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        Assert.Equal(HotkeyAction.ForceRegional, m.On(KeyEvent.RegionalDown, 100));
        Assert.Equal(HotkeyAction.StopAndTranscribe, m.On(KeyEvent.HoldUp, 1000));
    }

    [Fact]
    public void RegionalDuringHandsFree_AlsoForces()
    {
        var m = New();
        m.On(KeyEvent.HoldDown, 0);
        m.On(KeyEvent.HoldUp, 100);
        m.On(KeyEvent.HoldDown, 250);
        m.On(KeyEvent.HoldUp, 330);   // latched
        Assert.Equal(HotkeyAction.ForceRegional, m.On(KeyEvent.RegionalDown, 600));
        Assert.Equal(HotkeyAction.StopAndTranscribe, m.On(KeyEvent.HoldDown, 5000));
    }

    [Fact]
    public void RegionalWhileIdle_DoesNothing() =>
        Assert.Equal(HotkeyAction.None, New().On(KeyEvent.RegionalDown, 0));

    [Fact]
    public void KeyEventsWhileIdle_DoNothing()
    {
        var m = New();
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.OtherKeyDown, 0));
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.EscDown, 0));
        Assert.Equal(HotkeyAction.None, m.On(KeyEvent.HoldUp, 0));
    }
}
