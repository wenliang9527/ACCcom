using System;
using System.IO;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class HighlightServiceTests : IDisposable
{
    private readonly string _tempFile;
    private readonly HighlightService _sut;

    public HighlightServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"highlights_test_{Guid.NewGuid():N}.json");
        _sut = new HighlightService(_tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "hello world", string hex = "AA BB CC")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = DateTime.Now,
            Direction = direction,
            PortTag = "COM1",
            RawHex = hex,
            Text = text
        };
    }

    [Fact]
    public void AddRule_adds_to_collection()
    {
        var rule = new HighlightRule { Name = "Test", Pattern = "hello", Color = "#00FF00" };

        _sut.AddRule(rule);

        Assert.Single(_sut.Rules);
        Assert.Equal("Test", _sut.Rules[0].Name);
    }

    [Fact]
    public void AddRule_replaces_existing_by_name()
    {
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "hello", Color = "#00FF00" });
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "world", Color = "#FF0000" });

        Assert.Single(_sut.Rules);
        Assert.Equal("world", _sut.Rules[0].Pattern);
    }

    [Fact]
    public void RemoveRule_removes_existing_returns_true()
    {
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "hello", Color = "#00FF00" });

        var result = _sut.RemoveRule("Test");

        Assert.True(result);
        Assert.Empty(_sut.Rules);
    }

    [Fact]
    public void RemoveRule_nonexistent_returns_false()
    {
        var result = _sut.RemoveRule("NonExistent");

        Assert.False(result);
    }

    [Fact]
    public void GetHighlightColor_contains_match()
    {
        _sut.AddRule(new HighlightRule { Name = "Error", Pattern = "error", Color = "#FF0000" });
        var entry = MakeEntry(1, text: "this is an error message");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#FF0000", color);
    }

    [Fact]
    public void GetHighlightColor_no_match_returns_null()
    {
        _sut.AddRule(new HighlightRule { Name = "Error", Pattern = "error", Color = "#FF0000" });
        var entry = MakeEntry(1, text: "hello world");

        var color = _sut.GetHighlightColor(entry);

        Assert.Null(color);
    }

    [Fact]
    public void GetHighlightColor_exact_match()
    {
        _sut.AddRule(new HighlightRule { Name = "Exact", Pattern = "hello world", Color = "#0000FF", MatchType = HighlightMatchType.Exact });
        var entry = MakeEntry(1, text: "hello world");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#0000FF", color);
    }

    [Fact]
    public void GetHighlightColor_exact_no_match_partial()
    {
        _sut.AddRule(new HighlightRule { Name = "Exact", Pattern = "hello", Color = "#0000FF", MatchType = HighlightMatchType.Exact });
        var entry = MakeEntry(1, text: "hello world");

        var color = _sut.GetHighlightColor(entry);

        Assert.Null(color);
    }

    [Fact]
    public void GetHighlightColor_regex_match()
    {
        _sut.AddRule(new HighlightRule { Name = "Regex", Pattern = @"\d{3}", Color = "#FF00FF", MatchType = HighlightMatchType.Regex });
        var entry = MakeEntry(1, text: "code 123");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#FF00FF", color);
    }

    [Fact]
    public void GetHighlightColor_hex_match()
    {
        _sut.AddRule(new HighlightRule { Name = "HexRule", Pattern = "AA BB", Color = "#FFFF00", MatchHex = true });
        var entry = MakeEntry(1, hex: "AA BB CC DD");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#FFFF00", color);
    }

    [Fact]
    public void GetHighlightColor_direction_filter_rx()
    {
        _sut.AddRule(new HighlightRule { Name = "RXRule", Pattern = "hello", Color = "#00FF00", Direction = HighlightDirection.RX });
        var rxEntry = MakeEntry(1, direction: "RX", text: "hello");
        var txEntry = MakeEntry(2, direction: "TX", text: "hello");

        Assert.Equal("#00FF00", _sut.GetHighlightColor(rxEntry));
        Assert.Null(_sut.GetHighlightColor(txEntry));
    }

    [Fact]
    public void GetHighlightColor_disabled_rule_ignored()
    {
        _sut.AddRule(new HighlightRule { Name = "Disabled", Pattern = "hello", Color = "#FF0000", IsEnabled = false });
        var entry = MakeEntry(1, text: "hello");

        var color = _sut.GetHighlightColor(entry);

        Assert.Null(color);
    }

    [Fact]
    public void GetHighlightColor_priority_wins()
    {
        _sut.AddRule(new HighlightRule { Name = "Low", Pattern = "hello", Color = "#0000FF", Priority = 1 });
        _sut.AddRule(new HighlightRule { Name = "High", Pattern = "hello", Color = "#FF0000", Priority = 10 });
        var entry = MakeEntry(1, text: "hello");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#FF0000", color);
    }

    [Fact]
    public void GetHighlightColor_null_entry_returns_null()
    {
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "hello", Color = "#FF0000" });

        var color = _sut.GetHighlightColor(null!);

        Assert.Null(color);
    }

    [Fact]
    public void GetHighlightColor_no_rules_returns_null()
    {
        var entry = MakeEntry(1, text: "hello");

        var color = _sut.GetHighlightColor(entry);

        Assert.Null(color);
    }

    [Fact]
    public void Save_and_Load_roundtrip()
    {
        _sut.AddRule(new HighlightRule { Name = "Rule1", Pattern = "abc", Color = "#111111", Priority = 5, IsEnabled = true, MatchHex = false, Direction = HighlightDirection.RX, MatchType = HighlightMatchType.Contains });
        _sut.AddRule(new HighlightRule { Name = "Rule2", Pattern = "def", Color = "#222222", Priority = 10, IsEnabled = false, MatchHex = true, MatchType = HighlightMatchType.Regex });

        _sut.Save();

        var loaded = new HighlightService(_tempFile);
        loaded.Load();

        Assert.Equal(2, loaded.Rules.Count);
        Assert.Equal("Rule1", loaded.Rules[0].Name);
        Assert.Equal("abc", loaded.Rules[0].Pattern);
        Assert.Equal("#111111", loaded.Rules[0].Color);
        Assert.Equal(5, loaded.Rules[0].Priority);
        Assert.True(loaded.Rules[0].IsEnabled);
        Assert.False(loaded.Rules[0].MatchHex);
        Assert.Equal(HighlightDirection.RX, loaded.Rules[0].Direction);
        Assert.Equal(HighlightMatchType.Contains, loaded.Rules[0].MatchType);

        Assert.Equal("Rule2", loaded.Rules[1].Name);
        Assert.False(loaded.Rules[1].IsEnabled);
        Assert.True(loaded.Rules[1].MatchHex);
        Assert.Equal(HighlightMatchType.Regex, loaded.Rules[1].MatchType);
    }

    [Fact]
    public void Load_nonexistent_file_starts_empty()
    {
        var svc = new HighlightService(Path.Combine(Path.GetTempPath(), "nonexistent.json"));

        svc.Load();

        Assert.Empty(svc.Rules);
    }

    [Fact]
    public void Save_creates_file()
    {
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "x", Color = "#FFF" });

        _sut.Save();

        Assert.True(File.Exists(_tempFile));
    }

    [Fact]
    public void Load_corrupt_file_starts_empty()
    {
        File.WriteAllText(_tempFile, "not valid json {{{");

        _sut.Load();

        Assert.Empty(_sut.Rules);
    }

    [Fact]
    public void GetHighlightColor_case_insensitive_contains()
    {
        _sut.AddRule(new HighlightRule { Name = "Test", Pattern = "HELLO", Color = "#AA0000" });
        var entry = MakeEntry(1, text: "say hello to me");

        var color = _sut.GetHighlightColor(entry);

        Assert.Equal("#AA0000", color);
    }
}
