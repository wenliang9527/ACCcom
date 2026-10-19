using System.Globalization;
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
    public void FilterEntry_setSearchMatch_false_leaves_flag_untouched()
    {
        // Combined-view filter must not clobber F3 highlighting owned by the
        // active RX/TX list (and vice versa when the combined pane is active).
        var entry = MakeEntry(text: "hello world");
        entry.IsSearchMatch = true;

        var result = DataPanelFilter.FilterEntry(
            entry, "zzz", useRegex: false, showDirection: true, expressionEngine: null,
            errorSink: null, setSearchMatch: false);

        Assert.False(result);
        Assert.True(entry.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_empty_filter_setSearchMatch_false_keeps_flag()
    {
        var entry = MakeEntry();
        entry.IsSearchMatch = true;

        var result = DataPanelFilter.FilterEntry(
            entry, "", useRegex: false, showDirection: true, expressionEngine: null,
            errorSink: null, setSearchMatch: false);

        Assert.True(result);
        Assert.True(entry.IsSearchMatch);
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
    public void FilterEntry_expression_with_showDirection_false_still_hides_direction()
    {
        // Expression path is reached only after the direction gate.
        var engine = new PacketFilterEngine("text contains OK");
        var tx = MakeEntry(text: "OK done", direction: "TX");

        Assert.False(DataPanelFilter.FilterEntry(tx, "zzz", useRegex: false, showDirection: false, engine));
        Assert.False(tx.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_expression_setSearchMatch_false_leaves_flag()
    {
        var engine = new PacketFilterEngine("text contains OK");
        var entry = MakeEntry(text: "OK done");
        entry.IsSearchMatch = true;

        var result = DataPanelFilter.FilterEntry(
            entry, "zzz", useRegex: false, showDirection: true, engine,
            errorSink: null, setSearchMatch: false);

        Assert.True(result);
        Assert.True(entry.IsSearchMatch);
    }

    [Fact]
    public void FilterEntry_regex_setSearchMatch_false_leaves_flag()
    {
        var entry = MakeEntry(text: "hello");
        entry.IsSearchMatch = true;

        var result = DataPanelFilter.FilterEntry(
            entry, "zzz", useRegex: true, showDirection: true, expressionEngine: null,
            errorSink: null, setSearchMatch: false);

        Assert.False(result);
        Assert.True(entry.IsSearchMatch);
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

    [Fact]
    public void RegexFilterCache_concurrent_gets_share_one_instance_per_pattern()
    {
        // The pool used to sit behind a process-wide lock (taken by every row
        // of every filtered list on every refresh); it is now a
        // ConcurrentDictionary. Lock-freedom itself is not observable, but the
        // one-instance-per-pattern contract is.
        var seen = new System.Collections.Concurrent.ConcurrentBag<System.Text.RegularExpressions.Regex>();
        System.Threading.Tasks.Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 200; i++)
                seen.Add(RegexFilterCache.Get(@"shared\d+"));
        });

        Assert.Equal(1600, seen.Count);
        Assert.Single(seen.Distinct());
    }

    [Fact]
    public void Regex_filter_with_backtracking_bomb_times_out_instead_of_hanging()
    {
        // A user-typed catastrophic pattern used to pin the filter pass — and
        // the UI thread running it — for as long as the backtracking took.
        // Every cached regex now carries the same 2s timeout PatternMatcher
        // has, and FilterEntry surfaces it through the error sink.
        var errors = new List<string>();
        var entry = MakeEntry(text: new string('a', 40) + "b");

        var result = DataPanelFilter.FilterEntry(entry, "^(a+)+$", useRegex: true,
            showDirection: true, expressionEngine: null, errorSink: errors.Add);

        Assert.False(result);
        Assert.Contains(errors, e => e.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("10:30:00", ">=", "10:00:00", true)]
    [InlineData("10:30:00", "<", "11:00:00", true)]
    [InlineData("10:30:00", "==", "10:30:00", true)]
    [InlineData("10:30:00", "==", "10:00:00", false)]
    [InlineData("10:30:00", ">", "10:30:00", false)]
    [InlineData("10:30:00", "!=", "10:30:00", false)]
    public void Time_comparisons_use_numeric_semantics(string stamp, string op, string value, bool expected)
    {
        var engine = new PacketFilterEngine($"time {op} {value}");
        var entry = MakeEntry();
        entry.Timestamp = DateTime.Today + TimeSpan.Parse(stamp, CultureInfo.InvariantCulture);

        Assert.Equal(expected, engine.Matches(entry));
    }

    [Fact]
    public void Time_field_sub_second_precision_is_not_truncated()
    {
        // The old path formatted HH:mm:ss and compared strings, so 10:30:00.500
        // matched `time == 10:30:00`. Numeric comparison keeps the precision the
        // timestamp actually carries.
        var engine = new PacketFilterEngine("time == 10:30:00");
        var entry = MakeEntry();
        entry.Timestamp = DateTime.Today.AddHours(10).AddMinutes(30).AddMilliseconds(500);

        Assert.False(engine.Matches(entry));
    }
}
