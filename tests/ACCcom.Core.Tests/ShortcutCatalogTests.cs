using System.IO;
using System.Text.Json;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Every shortcut's description and group keys must resolve in both shipped
/// languages. The language files live in the WPF project; we locate the repo
/// root relative to this test assembly so a missing key fails the build here
/// instead of silently showing the raw key in the F1 window.
/// </summary>
public class ShortcutCatalogTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        // Test assembly: <root>/tests/ACCcom.Core.Tests/bin/Release/net8.0/
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "ACCcom", "Languages")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (src/ACCcom/Languages) from test assembly");
    }

    private static Dictionary<string, string> LoadLanguage(string code)
    {
        var path = Path.Combine(RepoRoot, "src", "ACCcom", "Languages", $"{code}.json");
        Assert.True(File.Exists(path), $"Language file missing: {path}");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"Could not parse {code}.json");
    }

    [Fact]
    public void All_DescriptionKeys_ResolveInBothLanguages()
    {
        var zh = LoadLanguage("zh-CN");
        var en = LoadLanguage("en-US");

        foreach (var shortcut in ShortcutCatalog.All)
        {
            Assert.True(zh.ContainsKey(shortcut.DescriptionKey),
                $"zh-CN missing key '{shortcut.DescriptionKey}' (shortcut '{shortcut.Keys}')");
            Assert.True(en.ContainsKey(shortcut.DescriptionKey),
                $"en-US missing key '{shortcut.DescriptionKey}' (shortcut '{shortcut.Keys}')");
        }
    }

    [Fact]
    public void All_GroupKeys_ResolveInBothLanguages()
    {
        var zh = LoadLanguage("zh-CN");
        var en = LoadLanguage("en-US");

        foreach (var group in ShortcutCatalog.Groups)
        {
            Assert.True(zh.ContainsKey(group.GroupKey), $"zh-CN missing group key '{group.GroupKey}'");
            Assert.True(en.ContainsKey(group.GroupKey), $"en-US missing group key '{group.GroupKey}'");
        }
    }

    [Fact]
    public void Catalog_HasNonEmptyGroups_AndUniqueKeys()
    {
        Assert.NotEmpty(ShortcutCatalog.Groups);
        var allKeys = ShortcutCatalog.All.Select(s => s.Keys).ToList();
        Assert.Equal(allKeys.Count, allKeys.Distinct().Count());
        Assert.All(ShortcutCatalog.Groups, g => Assert.NotEmpty(g.Shortcuts));
    }

    [Fact]
    public void EveryShortcut_HasNonEmptyKeysAndDescriptionKey()
    {
        Assert.All(ShortcutCatalog.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Keys));
            Assert.False(string.IsNullOrWhiteSpace(s.DescriptionKey));
        });
    }
}