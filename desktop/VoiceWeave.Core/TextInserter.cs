namespace VoiceWeave.Core;

public sealed record ClipSnapshot(IReadOnlyDictionary<uint, byte[]> Formats);

public interface IClipboard
{
    ClipSnapshot Snapshot();
    void SetText(string text);
    string? ReadText();
    void Restore(ClipSnapshot snapshot);
}

public interface IPaster
{
    /// <summary>Sends Ctrl+V, or Ctrl+Shift+V when <paramref name="terminal"/> is true.</summary>
    void CtrlV(bool terminal);
}

public sealed record FocusInfo(bool IsPassword, bool IsElevatedTarget, bool IsTerminal, string TextBeforeCursor);

public enum InsertOutcome { Pasted, SkippedPasswordField, SkippedElevatedTarget, NothingToInsert }

/// <summary>Inserts text at the cursor of the focused app via the clipboard, leaving the clipboard as it was.</summary>
public static class TextInserter
{
    private const int SetTextAttempts = 3;

    public static async Task<InsertOutcome> InsertAsync(
        IClipboard clipboard, IPaster paster, FocusInfo focus, string text, TimeSpan restoreDelay)
    {
        if (string.IsNullOrWhiteSpace(text)) return InsertOutcome.NothingToInsert;
        if (focus.IsPassword) return InsertOutcome.SkippedPasswordField;
        if (focus.IsElevatedTarget) return InsertOutcome.SkippedElevatedTarget;

        var snapshot = clipboard.Snapshot();
        try
        {
            var verified = false;
            for (var i = 0; i < SetTextAttempts && !verified; i++)
            {
                clipboard.SetText(text);
                verified = clipboard.ReadText() == text;
            }
            if (!verified)
                throw new InvalidOperationException("The clipboard did not accept the text; nothing was pasted.");

            paster.CtrlV(focus.IsTerminal);
            // The target app reads the clipboard after it receives the keystroke; restoring too early pastes the old content.
            if (restoreDelay > TimeSpan.Zero) await Task.Delay(restoreDelay);
            return InsertOutcome.Pasted;
        }
        finally
        {
            clipboard.Restore(snapshot);
        }
    }
}
