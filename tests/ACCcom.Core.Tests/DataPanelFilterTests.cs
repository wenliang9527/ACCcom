using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class DataPanelFilterTests
{
    private static LogEntry MakeEntry(string text = "hello world", string hex = "AA BB CC", string direction = "RX", int id = 1)
    {
        return new LogEntry
        {
            Id = id,
            Direction = direction,
            Text = text,
            RawHex = hex
        };
    }

    [Fact]
    public void FilterEntry_hidden_direction_returns_false()
    {
        var entry = MakeEntry(direction: "TX");

        var result = DataPanelFilter.FilterEntry(entry, "x", useRegex: false, showDirection: false, expressionEngine: null);

        Assert.False(result);
    }

    [Fact]
    public void FilterEntry_null_entry_throws()
    {
        // A null entry would NRE on entry.IsSearchMatch inside the filter
        // paths (empty-filter / regex / expression); fail at the entry point.
        Assert.Throws<ArgumentNullException>(() => DataPanelFilter.FilterEntry(null!, "x", useRegex: false, showDirection: true, expressionEngine: null));
        Assert.Throws<ArgumentNullException>(() => DataPanelFilter.FilterEntry(null!, "", useRegex: false, showDirection: true, expressionEngine: null));
        Assert.Throws<ArgumentNullException>(() => DataPanelFilter.FilterEntry(null!, "x", useRegex: false, showDirection: false, expressionEngine: null));
    }

    [Fact]
    public void FilterEntry_empty_filter_matches_all_and_clears_match_flag()
    {
        var entry = MakeEntry();

        var result = DataPanelFilter.FilterEntry(entry, "", useRegex: false, showDirection: true, expressionEngine: null);

        Assert.True(result);
        Assert.False(entry.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_plain_contains_case_insensitive()
    {
        var entry = MakeEntry(text: "HELLO World");

        var result = DataPanelFilter.FilterEntry(entry, "world", useRegex: false, showDirection: true, expressionEngine: null);

        Assert.True(result);
        Assert.True(entry.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_plain_matches_hex_too()
    {
        var entry = MakeEntry(text: "junk", hex: "AA BB CC DD");

        var result = DataPanelFilter.FilterEntry(entry, "bb cc", useRegex: false, showDirection: true, expressionEngine: null);

        Assert.True(result);
    }

    [Fact]
    public void FilterEntry_plain_no_match_returns_false_and_clears_flag()
    {
        var entry = MakeEntry(text: "hello");
        entry.IsSearchMatch = true; // stale from a previous filter pass

        var result = DataPanelFilter.FilterEntry(entry, "zzz", useRegex: false, showDirection: true, expressionEngine: null);

        Assert.False(result);
        Assert.False(entry.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_regex_matches_text_or_hex()
    {
        var entry = MakeEntry(text: "error code 42", hex: "AA BB");

        var result = DataPanelFilter.FilterEntry(entry, @"\d+", useRegex: true, showDirection: true, expressionEngine: null);

        Assert.True(result);
    }

    [Fact]
    public void FilterEntry_regex_no_match()
    {
        var entry = MakeEntry(text: "no digits here");

        var result = DataPanelFilter.FilterEntry(entry, @"\d+", useRegex: true, showDirection: true, expressionEngine: null);

        Assert.False(result);
    }

    [Fact]
    public void FilterEntry_invalid_regex_returns_false_and_reports_error()
    {
        var entry = MakeEntry();
        var errors = new List<string>();

        var result = DataPanelFilter.FilterEntry(entry, "[", useRegex: true, showDirection: true, expressionEngine: null, errorSink: errors.Add);

        Assert.False(result);
        Assert.Single(errors);
    }

    [Fact]
    public void FilterEntry_expression_engine_bypasses_plain_filter()
    {
        var engine = new PacketFilterEngine("text contains OK");
        var matching = MakeEntry(text: "OK done");
        var nonMatching = MakeEntry(text: "FAIL");

        Assert.True(DataPanelFilter.FilterEntry(matching, "zzz", useRegex: false, showDirection: true, engine));
        Assert.True(matching.IsSearchMatch);
        Assert.False(DataPanelFilter.FilterEntry(nonMatching, "", useRegex: false, showDirection: true, engine));
        Assert.False(nonMatching.IsSearchMatch);
    }

    [Fact]
    public void RegexFilterCache_reuses_compiled_regex()
    {
        var first = RegexFilterCache.Get(@"\d+");
        var second = RegexFilterCache.Get(@"\d+");

        Assert.Same(first, second);
    }

    [Fact]
    public void RegexFilterCache_distinct_patterns_do_not_interfere()
    {
        var a = RegexFilterCache.Get("foo");
        var b = RegexFilterCache.Get("bar");

        Assert.NotSame(a, b);
        Assert.Matches(a, "foo");
        Assert.DoesNotMatch(a, "bar");
    }

    [Fact]
    public void RegexFilterCache_stays_bounded()
    {
        // Fill beyond the 16-entry cap with unique patterns; the cache must not
        // throw or grow unboundedly (eviction happens internally).
        for (int i = 0; i < 64; i++)
        {
            var regex = RegexFilterCache.Get($"pattern_{i}");
            Assert.Matches(regex, $"pattern_{i}");
        }

        // A previously-cached entry may have been evicted, but re-fetch works.
        var reFetched = RegexFilterCache.Get("pattern_0");
        Assert.Matches(reFetched, "pattern_0");
    }
}
