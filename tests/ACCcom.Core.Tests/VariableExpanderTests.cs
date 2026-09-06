using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class VariableExpanderTests
{
    private static readonly DateTime FixedNow = new(2026, 9, 6, 14, 30, 45, 123);

    private static VariableExpander Create(params string[] seconds)
        => new(() => seconds.Length == 0 ? FixedNow : DateTime.Parse(seconds[0]));

    [Fact]
    public void Expand_returns_input_unchanged_without_placeholders()
    {
        var sut = Create();

        Assert.Equal("hello world", sut.Expand("hello world"));
    }

    [Fact]
    public void Expand_null_or_empty_returns_input()
    {
        var sut = Create();

        Assert.Null(sut.Expand(null!));
        Assert.Equal("", sut.Expand(""));
    }

    [Fact]
    public void Expand_timestamp_formats_expected()
    {
        var sut = Create();

        Assert.Equal("2026-09-06 14:30:45.123", sut.Expand("{{timestamp}}"));
    }

    [Fact]
    public void Expand_date_and_time_format_expected()
    {
        var sut = Create();

        Assert.Equal("2026-09-06", sut.Expand("{{date}}"));
        Assert.Equal("14:30:45", sut.Expand("{{time}}"));
    }

    [Fact]
    public void Expand_ticks_uses_same_clock()
    {
        var sut = Create();
        var expectedTicks = FixedNow.Ticks.ToString();

        Assert.Equal(expectedTicks, sut.Expand("{{ticks}}"));
    }

    [Fact]
    public void Expand_counter_increments_per_expansion()
    {
        var sut = Create();

        Assert.Equal("1", sut.Expand("{{counter}}"));
        Assert.Equal("2", sut.Expand("{{counter}}"));
        Assert.Equal("3", sut.Expand("{{counter}}"));
    }

    [Fact]
    public void Expand_counter_does_not_advance_without_placeholder()
    {
        var sut = Create();

        Assert.Equal("plain text", sut.Expand("plain text"));
        Assert.Equal("1", sut.Expand("{{counter}}"));
    }

    [Fact]
    public void Expand_multiple_placeholders_in_one_string()
    {
        var sut = Create();

        var result = sut.Expand("{{counter}} @ {{date}} {{time}} {{counter}}");

        // String.Replace expands every occurrence of a placeholder to the same
        // snapshot value, so both {{counter}} read 1 — preserved from the
        // original ViewModel semantics (a single send is one batch).
        Assert.Equal("1 @ 2026-09-06 14:30:45 1", result);
    }

    [Fact]
    public void Expand_counter_state_is_shared_across_calls()
    {
        var sut = Create();

        sut.Expand("first {{counter}}");
        sut.Expand("second {{counter}}");

        Assert.Equal(2, sut.Counter);
    }

    [Fact]
    public void Expand_partial_placeholder_left_alone()
    {
        var sut = Create();

        Assert.Equal("not {{a full placeholder", sut.Expand("not {{a full placeholder"));
    }
}