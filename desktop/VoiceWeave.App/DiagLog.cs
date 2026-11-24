using System.IO;

namespace VoiceWeave.App;

/// <summary>
/// A small troubleshooting log at %LOCALAPPDATA%\VoiceWeave\log.txt: what happened and how long it took, never what was said.
/// Trimmed when it gets large so it cannot grow without bound.
/// </summary>
public static class DiagLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static string Path => System.IO.Path.Combine(AppPaths.Root, "log.txt");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Root);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                    File.WriteAllLines(Path, File.ReadLines(Path).Skip(File.ReadLines(Path).Count() / 2));
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* logging must never break the app */ }
    }
}
