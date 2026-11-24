using VoiceWeave.Core;

namespace VoiceWeave.Win.Tests;

public class KeyMapTests
{
    [Fact]
    public void RightCtrl_MapsToHold()
    {
        Assert.Equal(KeyEvent.HoldDown, KeyMap.Map(0xA3, true));
        Assert.Equal(KeyEvent.HoldUp, KeyMap.Map(0xA3, false));
    }

    [Fact]
    public void Escape_MapsToEscDown_OnlyOnPress()
    {
        Assert.Equal(KeyEvent.EscDown, KeyMap.Map(0x1B, true));
        Assert.Null(KeyMap.Map(0x1B, false));
    }

    [Fact]
    public void OtherKeys_MapToOtherKeyDown_OnlyOnPress()
    {
        Assert.Equal(KeyEvent.OtherKeyDown, KeyMap.Map(0x41, true));
        Assert.Null(KeyMap.Map(0x41, false));
    }
}
