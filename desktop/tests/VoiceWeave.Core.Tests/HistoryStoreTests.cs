namespace VoiceWeave.Core.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vw-history-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "history.jsonl");

    public HistoryStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static HistoryEntry Entry(string text, DateTimeOffset when, string mode = "en") =>
        new(Guid.NewGuid(), when, text, mode, 1.2);

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewestFirst()
    {
        var h = new HistoryStore(Path_);
        h.Add(Entry("first", Now));
        h.Add(Entry("second", Now.AddMinutes(1)));
        Assert.Equal(new[] { "second", "first" }, h.All.Select(e => e.Text));
    }

    [Fact]
    public void SurvivesARestart_IncludingTeluguText()
    {
        var a = new HistoryStore(Path_);
        a.Add(Entry("nenu repu vastanu", Now, "te"));
        a.Add(Entry("నేను రేపు వస్తాను", Now.AddMinutes(1), "te"));
        var b = new HistoryStore(Path_);
        Assert.Equal(new[] { "నేను రేపు వస్తాను", "nenu repu vastanu" }, b.All.Select(e => e.Text));
        Assert.Equal("te", b.All[0].Mode);
        Assert.Equal(1.2, b.All[0].Seconds);
    }

    [Fact]
    public void Search_IsCaseInsensitive_AndMatchesAnywhere()
    {
        var h = new HistoryStore(Path_);
        h.Add(Entry("Please review the pull request", Now));
        h.Add(Entry("Send the report to Maria", Now.AddMinutes(1)));
        Assert.Equal(new[] { "Please review the pull request" }, h.Search("PULL").Select(e => e.Text));
        Assert.Equal(2, h.Search("  ").Count);          // a blank search shows everything
        Assert.Empty(h.Search("zebra"));
    }

    [Fact]
    public void Delete_RemovesOneEntry_AndPersists()
    {
        var h = new HistoryStore(Path_);
        var keep = Entry("keep me", Now);
        var drop = Entry("drop me", Now.AddMinutes(1));
        h.Add(keep);
        h.Add(drop);
        Assert.True(h.Delete(drop.Id));
        Assert.False(h.Delete(drop.Id));
        Assert.Equal(new[] { "keep me" }, new HistoryStore(Path_).All.Select(e => e.Text));
    }

    [Fact]
    public void Clear_EmptiesEverything_AndPersists()
    {
        var h = new HistoryStore(Path_);
        h.Add(Entry("a", Now));
        h.Clear();
        Assert.Empty(h.All);
        Assert.Empty(new HistoryStore(Path_).All);
    }

    [Fact]
    public void Cap_DropsTheOldestEntries()
    {
        var h = new HistoryStore(Path_, maxEntries: 3);
        for (var i = 0; i < 6; i++) h.Add(Entry($"entry {i}", Now.AddMinutes(i)));
        Assert.Equal(new[] { "entry 5", "entry 4", "entry 3" }, h.All.Select(e => e.Text));
        Assert.Equal(3, new HistoryStore(Path_, maxEntries: 3).All.Count);
    }

    [Fact]
    public void Retention_DropsEntriesOlderThanTheLimit()
    {
        var now = Now;
        var h = new HistoryStore(Path_, retention: TimeSpan.FromDays(30), clock: () => now);
        h.Add(Entry("old", Now.AddDays(-45)));
        h.Add(Entry("recent", Now.AddDays(-5)));
        Assert.Equal(new[] { "recent" }, h.All.Select(e => e.Text));
    }

    [Fact]
    public void Retention_AppliesWhenLoadingAnOldFile()
    {
        var first = new HistoryStore(Path_);
        first.Add(Entry("old", Now.AddDays(-45)));
        first.Add(Entry("recent", Now.AddDays(-5)));
        var later = new HistoryStore(Path_, retention: TimeSpan.FromDays(30), clock: () => Now);
        Assert.Equal(new[] { "recent" }, later.All.Select(e => e.Text));
    }

    [Fact]
    public void NoRetention_KeepsEverything()
    {
        var h = new HistoryStore(Path_, retention: null, clock: () => Now);
        h.Add(Entry("ancient", Now.AddDays(-4000)));
        Assert.Single(h.All);
    }

    [Fact]
    public void CorruptOrBlankLines_AreSkipped_NotFatal()
    {
        File.WriteAllText(Path_, "this is not json\n\n{\"broken\":\n");
        var h = new HistoryStore(Path_);
        Assert.Empty(h.All);
        h.Add(Entry("works", Now));
        Assert.Single(new HistoryStore(Path_).All);
    }

    [Fact]
    public void ChangedEvent_FiresOnAddDeleteAndClear()
    {
        var h = new HistoryStore(Path_);
        var count = 0;
        h.Changed += () => count++;
        var e = Entry("x", Now);
        h.Add(e);
        h.Delete(e.Id);
        h.Clear();
        Assert.Equal(3, count);
    }

    [Fact]
    public void EmptyText_IsNotStored()
    {
        var h = new HistoryStore(Path_);
        h.Add(Entry("   ", Now));
        Assert.Empty(h.All);
    }

    [Fact]
    public void ConcurrentAdds_AreAllKept()
    {
        var h = new HistoryStore(Path_);
        Parallel.For(0, 40, i => h.Add(Entry($"entry {i}", Now.AddSeconds(i))));
        Assert.Equal(40, h.All.Count);
        Assert.Equal(40, new HistoryStore(Path_).All.Count);
    }

    [Fact]
    public void MissingFolder_IsCreated()
    {
        var nested = Path.Combine(_dir, "a", "b", "history.jsonl");
        var h = new HistoryStore(nested);
        h.Add(Entry("hello", Now));
        Assert.True(File.Exists(nested));
    }
}
