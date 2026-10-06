using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class PatternMatcherTests
{
    [Fact]
    public void Matches_ContainsMode_MatchesSubstring()
    {
        var entry = new LogEntry { Direction = "RX", Text = "Hello World", RawHex = "48 65 6C 6C 6F" };

        Assert.True(PatternMatcher.Matches(entry, "World", "contains", false));
        Assert.False(PatternMatcher.Matches(entry, "xyz", "contains", false));
    }

    [Fact]
    public void Matches_ExactMode_MatchesExactly()
    {
        var entry = new LogEntry { Direction = "RX", Text = "Hello", RawHex = "48 65 6C 6C 6F" };

        Assert.True(PatternMatcher.Matches(entry, "Hello", "exact", false));
        Assert.False(PatternMatcher.Matches(entry, "Hell", "exact", false));
    }

    [Fact]
    public void Matches_RegexMode_MatchesPattern()
    {
        var entry = new LogEntry { Direction = "RX", Text = "Temperature: 25.5°C", RawHex = "" };

        Assert.True(PatternMatcher.Matches(entry, @"\d+\.\d+", "regex", false));
        Assert.False(PatternMatcher.Matches(entry, @"^\d{3}$", "regex", false));
    }

    [Fact]
    public void Matches_DirectionFilter_Works()
    {
        var rxEntry = new LogEntry { Direction = "RX", Text = "Test" };
        var txEntry = new LogEntry { Direction = "TX", Text = "Test" };

        Assert.True(PatternMatcher.Matches(rxEntry, "Test", "contains", false, "RX"));
        Assert.False(PatternMatcher.Matches(txEntry, "Test", "contains", false, "RX"));
        Assert.True(PatternMatcher.Matches(txEntry, "Test", "contains", false, "TX"));
    }

    [Fact]
    public void Matches_MatchHex_UsesRawHex()
    {
        var entry = new LogEntry { Direction = "RX", Text = "Hello", RawHex = "48 65 6C 6C 6F" };

        Assert.True(PatternMatcher.Matches(entry, "48 65", "contains", true));
        Assert.False(PatternMatcher.Matches(entry, "Hello", "contains", true));
    }

    [Fact]
    public void MatchesPattern_CaseInsensitive()
    {
        Assert.True(PatternMatcher.MatchesPattern("Hello World", "hello", "contains"));
        Assert.True(PatternMatcher.MatchesPattern("Hello", "hello", "exact"));
    }

    [Fact]
    public void TryRegexMatch_InvalidRegex_ReturnsFalse()
    {
        Assert.False(PatternMatcher.TryRegexMatch("test", "[invalid"));
    }

    [Fact]
    public void Matches_null_entry_returns_false()
    {
        Assert.False(PatternMatcher.Matches(null, "pattern", "contains", matchHex: false));
        Assert.False(PatternMatcher.Matches(null, "pattern", "exact", matchHex: false));
        Assert.False(PatternMatcher.Matches(null, "pattern", "regex", matchHex: false));
    }

    [Fact]
    public void Matches_null_entry_with_direction_returns_false()
    {
        Assert.False(PatternMatcher.Matches(null, "pattern", "contains", matchHex: false, direction: "RX"));
    }

    [Fact]
    public void MatchesPattern_null_pattern_returns_false()
    {
        // null pattern 语义上是"没有模式"而不是"空模式匹配一切";
        // 旧行为在 contains/exact 分支抛 ArgumentNullException、regex 分支
        // 在缓存键处 NRE。DataBufferWaiter 调用侧的防御收编进 API 本体。
        Assert.False(PatternMatcher.MatchesPattern("target", null, "contains"));
        Assert.False(PatternMatcher.MatchesPattern("target", null, "exact"));
        Assert.False(PatternMatcher.MatchesPattern("target", null, "regex"));
        Assert.False(PatternMatcher.TryRegexMatch("target", null));
    }

    [Fact]
    public void MatchesPattern_null_matchMode_degrades_to_contains()
    {
        // 旧行为:matchMode.Equals 在 null 上 NRE(调用侧 ?? "contains" 兜底,
        // 这里把同样的宽容收进 API 本体)。
        Assert.True(PatternMatcher.MatchesPattern("Hello", "ell", null));
        Assert.False(PatternMatcher.MatchesPattern("Hello", "xyz", null));
    }
}
