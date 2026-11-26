namespace VoiceWeave.Core.Tests;

public class SilenceEnderTests
{
    private static SilenceEnder New() => new(speechLevel: 0.01f, silenceMs: 2000, noSpeechMs: 6000);

    [Fact]
    public void FinishesAfterSpeechThenQuiet()
    {
        var e = New();
        e.Start(0);
        Assert.Equal(EndAction.None, e.OnLevel(0.05f, 500));
        Assert.Equal(EndAction.None, e.OnLevel(0.001f, 2000));
        Assert.Equal(EndAction.Finish, e.OnLevel(0.001f, 2600));
    }

    [Fact]
    public void SpeakingAgainRestartsTheQuietClock()
    {
        var e = New();
        e.Start(0);
        e.OnLevel(0.05f, 500);
        e.OnLevel(0.05f, 2400);
        Assert.Equal(EndAction.None, e.OnLevel(0.001f, 4000));
        Assert.Equal(EndAction.Finish, e.OnLevel(0.001f, 4500));
    }

    [Fact]
    public void GivesUpWhenNothingIsSaid()
    {
        var e = New();
        e.Start(1000);
        Assert.Equal(EndAction.None, e.OnLevel(0.001f, 6900));
        Assert.Equal(EndAction.Cancel, e.OnLevel(0.001f, 7000));
    }

    [Fact]
    public void QuietBeforeSpeakingNeverFinishes()
    {
        var e = New();
        e.Start(0);
        Assert.Equal(EndAction.None, e.OnLevel(0.001f, 3000)); // longer than the quiet limit, but nobody has spoken yet
    }

    [Fact]
    public void StartResetsEarlierState()
    {
        var e = New();
        e.Start(0);
        e.OnLevel(0.05f, 100);
        e.Start(10_000);
        Assert.Equal(EndAction.None, e.OnLevel(0.001f, 13_000));
    }
}
