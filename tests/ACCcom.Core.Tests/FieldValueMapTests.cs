using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FieldValueMapTests
{
    [Fact]
    public void Parse_key_value_pairs()
    {
        var result = FieldValueMap.Parse("OK=0,ERROR=1,TIMEOUT=2");

        Assert.NotNull(result);
        Assert.Equal("0", result["OK"]);
        Assert.Equal("1", result["ERROR"]);
        Assert.Equal("2", result["TIMEOUT"]);
    }

    [Fact]
    public void Parse_trims_key_and_value()
    {
        var parsed = FieldValueMap.Parse(" OK = 0 , ERROR = 1 ");
        Assert.NotNull(parsed);
        var result = parsed!;

        Assert.Equal("0", result["OK"]);
        Assert.Equal("1", result["ERROR"]);
    }

    [Fact]
    public void Parse_skips_segments_without_equals()
    {
        var parsed = FieldValueMap.Parse("OK=0,badsegment,ERROR=1");
        Assert.NotNull(parsed);
        var result = parsed!;

        Assert.Equal(2, result.Count);
        Assert.Equal("0", result["OK"]);
        Assert.Equal("1", result["ERROR"]);
    }

    [Fact]
    public void Parse_skips_empty_key()
    {
        var parsed = FieldValueMap.Parse("OK=0,=5");
        Assert.NotNull(parsed);
        var result = parsed!;

        Assert.Single(result);
        Assert.False(result.ContainsKey(""));
    }

    [Fact]
    public void Parse_empty_or_whitespace_returns_null()
    {
        Assert.Null(FieldValueMap.Parse(""));
        Assert.Null(FieldValueMap.Parse("   "));
        Assert.Null(FieldValueMap.Parse(null));
    }

    [Fact]
    public void Parse_all_invalid_segments_returns_null()
    {
        Assert.Null(FieldValueMap.Parse("justtext,another"));
    }

    [Fact]
    public void Serialize_produces_key_value_string()
    {
        var result = FieldValueMap.Serialize(new Dictionary<string, string> { ["OK"] = "0", ["ERR"] = "1" });

        Assert.Equal("OK=0,ERR=1", result);
    }

    [Fact]
    public void Serialize_null_or_empty_returns_empty_string()
    {
        Assert.Equal("", FieldValueMap.Serialize(null));
        Assert.Equal("", FieldValueMap.Serialize(new Dictionary<string, string>()));
    }

    [Fact]
    public void Parse_serialize_roundtrip()
    {
        var text = "A=1,B=2,C=3";

        var parsed = FieldValueMap.Parse(text);
        var serialized = FieldValueMap.Serialize(parsed);

        // Dictionary order may differ, but the pair set must be equivalent.
        var reparsed = FieldValueMap.Parse(serialized);
        Assert.Equal(parsed!, reparsed);
    }
}