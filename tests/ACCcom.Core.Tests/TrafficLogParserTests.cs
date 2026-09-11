using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TrafficLogParserTests
{
    [Fact]
    public void Parse_FullLine_ExtractsAllFields()
    {
        const string iso = "2026-09-11T08:30:12.3456789+08:00";
        var line = $"{{\"id\":7,\"tool\":\"send\",\"timestamp\":\"{iso}\",\"direction\":\"TX\",\"rawHex\":\"48 65 6C 6C 6F\",\"text\":\"Hello\",\"portTag\":\"a\"}}";

        Assert.True(TrafficLogParser.TryParseLine(line, out var entry));
        Assert.Equal("08:30:12.345", entry.Time);
        Assert.Equal("TX", entry.Direction);
        Assert.Equal("send", entry.Tool);
        Assert.Equal("a", entry.Tag);
        Assert.Equal("48 65 6C 6C 6F", entry.Hex);
        Assert.Equal("Hello", entry.Text);
    }

    [Fact]
    public void Parse_MissingFields_DefaultToEmpty()
    {
        const string line = "{\"direction\":\"RX\",\"rawHex\":\"00 FF\"}";

        Assert.True(TrafficLogParser.TryParseLine(line, out var entry));
        Assert.Equal("", entry.Time);
        Assert.Equal("RX", entry.Direction);
        Assert.Equal("", entry.Tool);
        Assert.Equal("", entry.Tag);
        Assert.Equal("00 FF", entry.Hex);
        Assert.Equal("", entry.Text);
    }

    [Fact]
    public void Parse_NullPortTagAndCjkText_Survives()
    {
        const string line = "{\"tool\":\"send\",\"direction\":\"TX\",\"rawHex\":\"E4 BD A0 E5 A5 BD\",\"text\":\"你好\",\"portTag\":null}";

        Assert.True(TrafficLogParser.TryParseLine(line, out var entry));
        Assert.Equal("", entry.Tag);
        Assert.Equal("你好", entry.Text);
        Assert.Equal("E4 BD A0 E5 A5 BD", entry.Hex);
    }

    [Fact]
    public void Parse_ShortTimestamp_KeptAsIs()
    {
        const string line = "{\"timestamp\":\"08:30:12\"}";

        Assert.True(TrafficLogParser.TryParseLine(line, out var entry));
        Assert.Equal("08:30:12", entry.Time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("just text")]
    [InlineData("[]")]
    public void Parse_MalformedLine_ReturnsFalse(string line)
        => Assert.False(TrafficLogParser.TryParseLine(line, out _));
}
