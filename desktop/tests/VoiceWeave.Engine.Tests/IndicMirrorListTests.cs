using VoiceWeave.Engine;

namespace VoiceWeave.Engine.Tests;

/// <summary>Keeps the mirror's file list (used by the workflow that publishes our copies) in step with the hashes the app pins.</summary>
public class IndicMirrorListTests
{
    private const string MirrorBase = "https://github.com/techgniouss/VoiceWeave-Desktop/releases/download/mirror-indic-v1/";

    private static string ListPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "mirror", "indic-files.txt");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "desktop", "mirror", "indic-files.txt");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("desktop/mirror/indic-files.txt was not found above the test folder.");
    }

    private static Dictionary<string, (string Sha, long Size, string Url)> Listed() =>
        File.ReadAllLines(ListPath())
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(p => p[0], p => (p[1], long.Parse(p[2]), p[3]));

    public static IEnumerable<object[]> Languages() =>
        new[] { Language.Telugu, Language.Tamil, Language.Hindi }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryIndicFile_IsOnTheMirrorList_WithTheSameHashSizeAndOriginalAddress(Language language)
    {
        var listed = Listed();
        foreach (var spec in LanguagePacks.For(language).Files)
        {
            Assert.StartsWith(MirrorBase, spec.Url);
            var name = spec.Url[MirrorBase.Length..];
            Assert.True(listed.TryGetValue(name, out var entry), $"{name} is missing from indic-files.txt");
            Assert.Equal(spec.Sha256, entry.Sha);
            Assert.Equal(spec.Size, entry.Size);
            Assert.Equal(new[] { entry.Url }, spec.FallbackUrls);
        }
    }

    [Fact]
    public void TheListHasNothingTheAppDoesNotUse()
    {
        var used = new[] { Language.Telugu, Language.Tamil, Language.Hindi }
            .SelectMany(l => LanguagePacks.For(l).Files).Select(s => s.Url[MirrorBase.Length..]).ToHashSet();
        Assert.Equal(used.OrderBy(x => x), Listed().Keys.OrderBy(x => x));
    }
}
