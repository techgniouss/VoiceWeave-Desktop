namespace VoiceWeave.Core.Tests;

public class QuietGateTests
{
    private static float[] Quiet() => new float[480];
    private static float[] Loud() => Enumerable.Repeat(0.1f, 480).ToArray();

    private static QuietGate New() => new(level: 0.01f, hangoverSamples: 960, leadChunks: 3);

    [Fact]
    public void QuietRoom_PassesNothing()
    {
        var gate = New();
        for (var i = 0; i < 50; i++) Assert.Empty(gate.Process(Quiet(), out _));
    }

    [Fact]
    public void SoundOpensTheGate_WithTheLeadInAndARestart()
    {
        var gate = New();
        var before = new[] { Quiet(), Quiet(), Quiet(), Quiet(), Quiet() };
        foreach (var chunk in before) gate.Process(chunk, out _);
        var loud = Loud();
        var passed = gate.Process(loud, out var restart);
        Assert.True(restart);
        Assert.Equal(4, passed.Count);                      // the last three quiet chunks, then the loud one
        Assert.Same(loud, passed[^1]);
        Assert.Same(before[^1], passed[^2]);
    }

    [Fact]
    public void StaysOpenForAWhileAfterTheSoundStops_ThenCloses()
    {
        var gate = New();
        gate.Process(Loud(), out _);
        Assert.Single(gate.Process(Quiet(), out var restart));   // 480 quiet samples: still within the hangover
        Assert.False(restart);
        Assert.Single(gate.Process(Quiet(), out _));             // 960: at the limit
        Assert.Empty(gate.Process(Quiet(), out _));              // 1440: closed
    }

    [Fact]
    public void ReopeningAsksForAFreshStartAgain()
    {
        var gate = New();
        gate.Process(Loud(), out _);
        for (var i = 0; i < 5; i++) gate.Process(Quiet(), out _);
        gate.Process(Loud(), out var restart);
        Assert.True(restart);
    }

    [Fact]
    public void Reset_ClosesTheGate()
    {
        var gate = New();
        gate.Process(Loud(), out _);
        gate.Reset();
        Assert.Empty(gate.Process(Quiet(), out _));
    }
}
