namespace VoiceWeave.Core.Tests;

public class TextInserterTests
{
    private sealed class FakeClipboard : IClipboard
    {
        public Dictionary<uint, byte[]> Formats = new() { [13] = new byte[] { 9, 9 }, [8] = new byte[] { 1, 2, 3 } }; // pretend text + image
        public string? Text;
        public bool CorruptReadBack;
        public int SetCount;
        public ClipSnapshot Snapshot() => new(new Dictionary<uint, byte[]>(Formats));
        public void SetText(string t) { Text = t; SetCount++; Formats = new() { [13] = new byte[] { 7 } }; }
        public string? ReadText() => CorruptReadBack ? "stale" : Text;
        public void Restore(ClipSnapshot s) { Formats = new Dictionary<uint, byte[]>(s.Formats); Text = null; }
    }

    private sealed class FakePaster : IPaster
    {
        public readonly List<bool> Calls = new();
        public string? ClipboardTextAtPaste;
        public FakeClipboard? Cb;
        public void CtrlV(bool terminal) { Calls.Add(terminal); ClipboardTextAtPaste = Cb?.Text; }
    }

    private static readonly FocusInfo Normal = new(false, false, false, "");

    [Fact]
    public async Task ClipboardRestoredAfterPaste_AndPasteSawNewText()
    {
        var cb = new FakeClipboard();
        var p = new FakePaster { Cb = cb };
        var outcome = await TextInserter.InsertAsync(cb, p, Normal, "hello", TimeSpan.Zero);
        Assert.Equal(InsertOutcome.Pasted, outcome);
        Assert.Equal("hello", p.ClipboardTextAtPaste);
        Assert.Equal(new byte[] { 1, 2, 3 }, cb.Formats[8]); // non-text format preserved
        Assert.Equal(new byte[] { 9, 9 }, cb.Formats[13]);
    }

    [Fact]
    public async Task SetTextNotVerified_ThrowsWithoutPasting_AndRestoresClipboard()
    {
        var cb = new FakeClipboard { CorruptReadBack = true };
        var p = new FakePaster { Cb = cb };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TextInserter.InsertAsync(cb, p, Normal, "hello", TimeSpan.Zero));
        Assert.Empty(p.Calls);
        Assert.Equal(new byte[] { 1, 2, 3 }, cb.Formats[8]);
        Assert.True(cb.SetCount >= 2); // retried
    }

    [Fact]
    public async Task Terminal_UsesCtrlShiftV()
    {
        var cb = new FakeClipboard();
        var p = new FakePaster { Cb = cb };
        await TextInserter.InsertAsync(cb, p, Normal with { IsTerminal = true }, "ls", TimeSpan.Zero);
        Assert.Equal(new[] { true }, p.Calls);
    }

    [Fact]
    public async Task PasswordField_SkipsWithoutTouchingClipboard()
    {
        var cb = new FakeClipboard();
        var p = new FakePaster { Cb = cb };
        var outcome = await TextInserter.InsertAsync(cb, p, Normal with { IsPassword = true }, "secret", TimeSpan.Zero);
        Assert.Equal(InsertOutcome.SkippedPasswordField, outcome);
        Assert.Equal(0, cb.SetCount);
        Assert.Empty(p.Calls);
    }

    [Fact]
    public async Task ElevatedTarget_SkipsWithoutTouchingClipboard()
    {
        var cb = new FakeClipboard();
        var p = new FakePaster { Cb = cb };
        var outcome = await TextInserter.InsertAsync(cb, p, Normal with { IsElevatedTarget = true }, "x", TimeSpan.Zero);
        Assert.Equal(InsertOutcome.SkippedElevatedTarget, outcome);
        Assert.Equal(0, cb.SetCount);
        Assert.Empty(p.Calls);
    }

    [Fact]
    public async Task EmptyText_DoesNothing()
    {
        var cb = new FakeClipboard();
        var p = new FakePaster { Cb = cb };
        var outcome = await TextInserter.InsertAsync(cb, p, Normal, "  ", TimeSpan.Zero);
        Assert.Equal(InsertOutcome.NothingToInsert, outcome);
        Assert.Equal(0, cb.SetCount);
    }
}
