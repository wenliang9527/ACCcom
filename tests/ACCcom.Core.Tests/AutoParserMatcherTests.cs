using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class AutoParserMatcherTests
{
    [Fact]
    public void Fingerprint_MatchesData_WithCorrectHeader()
    {
        var fp = new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4
        };

        var data = new byte[] { 0xA5, 0x5A, 0x01, 0x02 };
        Assert.True(fp.Matches(data));
    }

    [Fact]
    public void Fingerprint_RejectsData_WithWrongHeader()
    {
        var fp = new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4
        };

        var data = new byte[] { 0xFF, 0xFE, 0x01, 0x02 };
        Assert.False(fp.Matches(data));
    }

    [Fact]
    public void Fingerprint_RejectsData_TooShort()
    {
        var fp = new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4
        };

        var data = new byte[] { 0xA5, 0x5A };
        Assert.False(fp.Matches(data));
    }

    [Fact]
    public void Fingerprint_MatchesData_WithCommandCode()
    {
        var fp = new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4,
            CommandOffset = 2,
            CommandValues = new byte[] { 0x01, 0x02, 0x03 }
        };

        var data = new byte[] { 0xA5, 0x5A, 0x01, 0x00 };
        Assert.True(fp.Matches(data));

        data = new byte[] { 0xA5, 0x5A, 0x04, 0x00 };
        Assert.False(fp.Matches(data));
    }

    [Fact]
    public void Fingerprint_MatchesData_NoHeader()
    {
        var fp = new ParserFingerprint
        {
            Name = "Test",
            MinLength = 2
        };

        var data = new byte[] { 0x01, 0x02 };
        Assert.True(fp.Matches(data));
    }

    [Fact]
    public void Fingerprint_FromSchema_ExtractsHeader()
    {
        var schema = new ProtocolSchema
        {
            Name = "Test",
            MinLength = 4,
            Frame = new FrameSchema
            {
                Header = "A5 5A"
            }
        };

        var fp = ParserFingerprint.FromSchema(schema);
        Assert.Equal("Test", fp.Name);
        Assert.Equal("A55A", fp.HeaderHex);
        Assert.Equal(2, fp.HeaderLength);
        Assert.Equal(4, fp.MinLength);
    }

    [Fact]
    public void Fingerprint_FromSchema_ExtractsAutoMatch()
    {
        var schema = new ProtocolSchema
        {
            Name = "Test",
            MinLength = 6,
            AutoMatch = new AutoMatchConfig
            {
                Enabled = true,
                Priority = 10,
                HeaderPattern = "FF FE",
                CommandOffset = 2,
                KnownCommands = new[] { "0x01", "0x02" }
            }
        };

        var fp = ParserFingerprint.FromSchema(schema);
        Assert.Equal(10, fp.Priority);
        Assert.Equal("FFFE", fp.HeaderHex);
        Assert.Equal(2, fp.CommandOffset);
        Assert.Equal(new byte[] { 0x01, 0x02 }, fp.CommandValues);
    }

    [Fact]
    public void Matcher_ReturnsNull_WhenNoFingerprints()
    {
        var matcher = new AutoParserMatcher();
        var result = matcher.MatchParser(new byte[] { 0x01, 0x02 });
        Assert.Null(result);
    }

    [Fact]
    public void Matcher_MatchesBestParser_ByPriority()
    {
        var matcher = new AutoParserMatcher();

        matcher.UpdateFingerprint("Low", new ParserFingerprint
        {
            Name = "Low",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4,
            Priority = 1
        });

        matcher.UpdateFingerprint("High", new ParserFingerprint
        {
            Name = "High",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4,
            Priority = 10
        });

        var data = new byte[] { 0xA5, 0x5A, 0x01, 0x02 };
        var result = matcher.MatchParser(data);
        Assert.Equal("High", result);
    }

    [Fact]
    public void Matcher_ReturnsNull_WhenNoMatch()
    {
        var matcher = new AutoParserMatcher();

        matcher.UpdateFingerprint("Test", new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4
        });

        var data = new byte[] { 0xFF, 0xFE, 0x01, 0x02 };
        var result = matcher.MatchParser(data);
        Assert.Null(result);
    }

    [Fact]
    public void Matcher_GetAllMatches_ReturnsSortedMatches()
    {
        var matcher = new AutoParserMatcher();

        matcher.UpdateFingerprint("Low", new ParserFingerprint
        {
            Name = "Low",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4,
            Priority = 1
        });

        matcher.UpdateFingerprint("High", new ParserFingerprint
        {
            Name = "High",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4,
            Priority = 10
        });

        var data = new byte[] { 0xA5, 0x5A, 0x01, 0x02 };
        var matches = matcher.GetAllMatches(data);
        Assert.Equal(2, matches.Count);
        Assert.Equal("High", matches[0]);
        Assert.Equal("Low", matches[1]);
    }

    [Fact]
    public void Matcher_RemoveFingerprint_Works()
    {
        var matcher = new AutoParserMatcher();

        matcher.UpdateFingerprint("Test", new ParserFingerprint
        {
            Name = "Test",
            HeaderHex = "A55A",
            HeaderLength = 2,
            MinLength = 4
        });

        Assert.Equal(1, matcher.Count);

        matcher.RemoveFingerprint("Test");
        Assert.Equal(0, matcher.Count);
    }

    [Fact]
    public void Matcher_Clear_Works()
    {
        var matcher = new AutoParserMatcher();

        matcher.UpdateFingerprint("A", new ParserFingerprint { Name = "A" });
        matcher.UpdateFingerprint("B", new ParserFingerprint { Name = "B" });

        Assert.Equal(2, matcher.Count);

        matcher.Clear();
        Assert.Equal(0, matcher.Count);
    }
}
