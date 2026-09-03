using System.IO;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>Coverage for the trigger SaveToFile action's path-resolution rules
/// and the trigger-firing code paths in TriggerService that are exercised via
/// the resolver helper. Behaviour that lives in the WPF ViewModel
/// (<c>TriggerViewModel.OnTriggerFired</c>) is covered by viewmodel-level tests
/// in a separate file that takes a dependency on the UI assembly.</summary>
public class TriggerPathResolverTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var p in _tempPaths)
        {
            try
            {
                if (File.Exists(p)) File.Delete(p);
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void DataDirectory_IsUnderLocalAppData()
    {
        // Sanity check: keep this dependency explicit so a future move doesn't
        // silently start writing to a surprising location.
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            TriggerPathResolver.DataDirectory);
    }

    [Fact]
    public void Resolve_RelativePath_AnchorsAtDataDirectory()
    {
        var resolved = TriggerPathResolver.Resolve("payloads.log");
        Assert.Equal(Path.Combine(TriggerPathResolver.DataDirectory, "payloads.log"), resolved);
    }

    [Fact]
    public void Resolve_AbsolutePath_IsHonouredAsIs()
    {
        var absolute = Path.Combine(Path.GetTempPath(), $"tw_{Guid.NewGuid():N}.log");
        _tempPaths.Add(absolute);
        var resolved = TriggerPathResolver.Resolve(absolute);
        Assert.Equal(absolute, resolved);
    }

    [Fact]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", TriggerPathResolver.Resolve(""));
        Assert.Equal("", TriggerPathResolver.Resolve("   "));
    }

    [Fact]
    public void Resolve_NestedRelativePath_PreservesSubdirs()
    {
        // `nested/out.log` should land at DataDirectory/nested/out.log so users
        // can group trigger outputs by purpose.
        var resolved = TriggerPathResolver.Resolve(Path.Combine("nested", "out.log"));
        Assert.Equal(Path.Combine(TriggerPathResolver.DataDirectory, "nested", "out.log"), resolved);
    }

    /// <summary>SaveRules/LoadRules round-trip through a resolver-style path
    /// (DataDirectory/triggers.json is what the ViewModel now persists to).</summary>
    [Fact]
    public void SaveLoad_Roundtrip_WithResolverStylePath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"trg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        _tempPaths.Add(Path.Combine(tempDir, "triggers.json"));

        try
        {
            var rules = new List<TriggerRule>
            {
                new()
                {
                    Name = "OnError",
                    Pattern = "ERROR",
                    Action = TriggerAction.PlaySound,
                    Enabled = true
                },
                new()
                {
                    Name = "LogRx",
                    Pattern = "0x55",
                    MatchHex = true,
                    Direction = "RX",
                    Action = TriggerAction.LogMessage,
                    ActionParameter = "received 0x55"
                }
            };
            var filePath = Path.Combine(tempDir, "triggers.json");

            TriggerService.SaveRules(rules, filePath);
            var loaded = TriggerService.LoadRules(filePath);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("OnError", loaded[0].Name);
            Assert.Equal(TriggerAction.PlaySound, loaded[0].Action);
            Assert.Equal("LogRx", loaded[1].Name);
            Assert.True(loaded[1].MatchHex);
            Assert.Equal("RX", loaded[1].Direction);
            Assert.Equal("received 0x55", loaded[1].ActionParameter);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* cleanup */ }
        }
    }
}