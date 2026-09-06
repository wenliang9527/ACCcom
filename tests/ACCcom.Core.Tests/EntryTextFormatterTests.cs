using System.Text;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class EntryTextFormatterTests
{
    private static LogEntry MakeEntry(int id, string text = "", string hex = "", DateTime? ts = null)
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = ts ?? new DateTime(2026, 9, 6, 14, 30, 45, 123),
            Text = text,
            RawHex = hex
        };
    }

    [Fact]
    public void Format_text_only_entry()
    {
        var entry = MakeEntry(1, text: "hello");

        var result = EntryTextFormatter.Format(new[] { entry }, "RX");

        Assert.Equal("[14:30:45.123][RX][TXT] hello" + Environment.NewLine, result);
    }

    [Fact]
    public void Format_hex_only_entry()
    {
        var entry = MakeEntry(1, hex: "AA BB CC");

        var result = EntryTextFormatter.Format(new[] { entry }, "TX");

        Assert.Equal("[14:30:45.123][TX][HEX] AA BB CC" + Environment.NewLine, result);
    }

    [Fact]
    public void Format_entry_with_both_produces_two_lines()
    {
        var entry = MakeEntry(1, text: "hello", hex: "AA BB");

        var result = EntryTextFormatter.Format(new[] { entry }, "RX");

        Assert.Equal(
            "[14:30:45.123][RX][HEX] AA BB" + Environment.NewLine +
            "[14:30:45.123][RX][TXT] hello" + Environment.NewLine,
            result);
    }

    [Fact]
    public void Format_empty_string_fields_are_omitted()
    {
        var entry = MakeEntry(1, text: "", hex: "");

        var result = EntryTextFormatter.Format(new[] { entry }, "RX");

        Assert.Equal("", result);
    }

    [Fact]
    public void Format_preserves_millisecond_precision()
    {
        var entry = MakeEntry(1, text: "x", ts: new DateTime(2026, 1, 2, 3, 4, 5, 678));

        var result = EntryTextFormatter.Format(new[] { entry }, "RX");

        Assert.Contains("03:04:05.678", result);
    }

    [Fact]
    public void Format_multiple_entries_in_order()
    {
        var entries = new[]
        {
            MakeEntry(1, text: "first"),
            MakeEntry(2, text: "second")
        };

        var result = EntryTextFormatter.Format(entries, "TX");

        Assert.Contains("[TXT] first", result);
        Assert.Contains("[TXT] second", result);
        Assert.True(result.IndexOf("first", StringComparison.Ordinal) < result.IndexOf("second", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_appends_to_existing_builder()
    {
        var sb = new StringBuilder("preamble" + Environment.NewLine);
        var entry = MakeEntry(1, text: "hello");

        EntryTextFormatter.Format(new[] { entry }, "RX", sb);

        Assert.StartsWith("preamble", sb.ToString());
        Assert.EndsWith("[TXT] hello" + Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void Format_null_hex_or_text_treated_as_empty()
    {
        var entry = MakeEntry(1);
        entry.Text = null!;
        entry.RawHex = "AA";

        var result = EntryTextFormatter.Format(new[] { entry }, "RX");

        // Only the hex line, no NRE on null Text.
        Assert.Equal("[14:30:45.123][RX][HEX] AA" + Environment.NewLine, result);
    }

    [Fact]
    public void Format_empty_input_returns_empty_string()
    {
        var result = EntryTextFormatter.Format(Array.Empty<LogEntry>(), "RX");

        Assert.Equal("", result);
    }
}