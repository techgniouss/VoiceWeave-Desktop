using Microsoft.Win32;

namespace VoiceWeave.Win.Tests;

public class StartupRegistrationTests : IDisposable
{
    private readonly string _key = @"Software\VoiceWeaveTests\" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);

    [Fact]
    public void DefaultsToOff()
    {
        var s = new StartupRegistration(_key, "VoiceWeave");
        Assert.False(s.IsEnabled());
    }

    [Fact]
    public void EnableThenDisable_RoundTrips()
    {
        var s = new StartupRegistration(_key, "VoiceWeave");
        s.Set(true, @"C:\Apps\VoiceWeave.exe");
        Assert.True(s.IsEnabled());
        using (var k = Registry.CurrentUser.OpenSubKey(_key))
            Assert.Equal("\"C:\\Apps\\VoiceWeave.exe\" --background", k!.GetValue("VoiceWeave"));
        s.Set(false, @"C:\Apps\VoiceWeave.exe");
        Assert.False(s.IsEnabled());
    }

    [Fact]
    public void DisablingWhenNotEnabled_DoesNotThrow()
    {
        var s = new StartupRegistration(_key, "VoiceWeave");
        s.Set(false, "x");
        Assert.False(s.IsEnabled());
    }
}
