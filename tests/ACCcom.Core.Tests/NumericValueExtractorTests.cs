using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class NumericValueExtractorTests
{
    [Fact]
    public void Extract_key_value_segments()
    {
        var result = NumericValueExtractor.Extract("temp=23.5 ok=1");

        Assert.Equal(new[] { 23.5, 1.0 }, result);
    }

    [Fact]
    public void Extract_colon_separated()
    {
        var result = NumericValueExtractor.Extract("temp:12.75");

        Assert.Equal(new[] { 12.75 }, result);
    }

    [Fact]
    public void Extract_negative_values()
    {
        var result = NumericValueExtractor.Extract("delta=-4.5");

        Assert.Equal(new[] { -4.5 }, result);
    }

    [Fact]
    public void Extract_integers_in_key_value()
    {
        var result = NumericValueExtractor.Extract("count=42");

        Assert.Equal(new[] { 42.0 }, result);
    }

    [Fact]
    public void Extract_fallback_to_standalone_decimals_when_no_key_value()
    {
        var result = NumericValueExtractor.Extract("rx 12.3 tx -4.5");

        Assert.Equal(new[] { 12.3, -4.5 }, result);
    }

    [Fact]
    public void Extract_key_value_regex_also_catches_space_separated_numbers()
    {
        // KeyValueRegex's optional [=:]? lets it match numbers that are merely
        // space-separated (" 9.9"), so the first pass catches both — the
        // standalone fallback only fires when the first pass finds nothing.
        var result = NumericValueExtractor.Extract("a=1 9.9");

        Assert.Equal(new[] { 1.0, 9.9 }, result);
    }

    [Fact]
    public void Extract_empty_input_returns_empty()
    {
        Assert.Empty(NumericValueExtractor.Extract(""));
        Assert.Empty(NumericValueExtractor.Extract("   "));
        Assert.Empty(NumericValueExtractor.Extract(null!));
    }

    [Fact]
    public void Extract_no_numbers_returns_empty()
    {
        Assert.Empty(NumericValueExtractor.Extract("hello world"));
    }

    [Fact]
    public void Extract_mixed_text_with_key_values()
    {
        var result = NumericValueExtractor.Extract("voltage=5.2 current=0.34 status:OK");

        Assert.Equal(new[] { 5.2, 0.34 }, result);
    }

    [Fact]
    public void Extract_parse_failure_skipped()
    {
        // The regex matches a trailing bare sign like "x=-", but TryParse fails
        // and the token is dropped — no crash, no garbage value.
        var result = NumericValueExtractor.Extract("x=- y=3");

        Assert.Equal(new[] { 3.0 }, result);
    }

    [Fact]
    public void Extract_multiple_matches_in_one_segment()
    {
        var result = NumericValueExtractor.Extract("a=1 b=2.5 c=3");

        Assert.Equal(new[] { 1.0, 2.5, 3.0 }, result);
    }
}