namespace VoiceWeave.Core.Tests;

public class LevelWindowTests
{
    private static float[] Tone(float amplitude, int n = 480) => Enumerable.Repeat(amplitude, n).ToArray();

    [Fact]
    public void Empty_IsSilent() => Assert.Equal(0f, new LevelWindow().Peak);

    [Fact]
    public void Peak_IsTheLoudestPiece()
    {
        var w = new LevelWindow();
        w.Push(Tone(0.01f));
        w.Push(Tone(0.2f));
        w.Push(Tone(0.05f));
        Assert.Equal(0.2f, w.Peak, 3);
    }

    [Fact]
    public void OldLoudSound_FallsOutOfTheWindow()
    {
        var w = new LevelWindow(pieces: 4);
        w.Push(Tone(0.5f));
        for (var i = 0; i < 4; i++) w.Push(Tone(0.01f));
        Assert.Equal(0.01f, w.Peak, 3);
    }

    [Fact]
    public void Clear_ForgetsEverything()
    {
        var w = new LevelWindow();
        w.Push(Tone(0.3f));
        w.Clear();
        Assert.Equal(0f, w.Peak);
    }
}
