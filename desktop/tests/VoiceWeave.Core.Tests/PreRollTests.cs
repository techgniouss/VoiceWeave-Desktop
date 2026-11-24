namespace VoiceWeave.Core.Tests;

public class PreRollTests
{
    [Fact]
    public void KeepsOnlyLast500ms_At16k()
    {
        var p = new PreRoll(16000, 500);
        p.Push(Enumerable.Range(0, 20000).Select(i => (float)i).ToArray());
        var got = p.Take();
        Assert.Equal(8000, got.Length);
        Assert.Equal(12000f, got[0]);
        Assert.Equal(19999f, got[^1]);
    }

    [Fact]
    public void Take_ReturnsInOrder_AndClears()
    {
        var p = new PreRoll(16000, 500);
        p.Push(new float[] { 1, 2, 3 });
        p.Push(new float[] { 4, 5 });
        Assert.Equal(new float[] { 1, 2, 3, 4, 5 }, p.Take());
        Assert.Empty(p.Take());
    }

    [Fact]
    public void WrapAround_KeepsOrder()
    {
        var p = new PreRoll(10, 1000); // capacity 10
        p.Push(Enumerable.Range(1, 7).Select(i => (float)i).ToArray());
        p.Push(Enumerable.Range(8, 6).Select(i => (float)i).ToArray()); // 13 total
        Assert.Equal(Enumerable.Range(4, 10).Select(i => (float)i).ToArray(), p.Take());
    }
}
