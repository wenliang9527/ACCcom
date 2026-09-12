using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TimestampedFileNameTests
{
    [Fact]
    public void Build_WithPrefixTagExtension_ProducesExpectedName()
    {
        var now = new DateTime(2026, 9, 9, 14, 30, 0);
        var name = TimestampedFileName.Build("ACCCOM", now, "serial", "txt");
        Assert.Equal("ACCCOM_serial_20260909_143000.txt", name);
    }

    [Fact]
    public void Build_WithoutTag_OmitsTagSegment()
    {
        var now = new DateTime(2026, 9, 9, 14, 30, 0);
        var name = TimestampedFileName.Build("MODBUS_Log", now);
        Assert.Equal("MODBUS_Log_20260909_143000", name);
    }

    [Fact]
    public void Build_WithoutExtension_OmitsDot()
    {
        var now = new DateTime(2026, 9, 9, 14, 30, 0);
        var name = TimestampedFileName.Build("session", now, extension: "jsonl");
        Assert.Equal("session_20260909_143000.jsonl", name);
    }

    [Fact]
    public void Build_NullTagAndExtension_StillTimestamped()
    {
        var now = new DateTime(2026, 9, 9, 0, 5, 7);
        var name = TimestampedFileName.Build("ACCCOM", now, null, null);
        Assert.Equal("ACCCOM_20260909_000507", name);
    }

    [Fact]
    public void Build_EmptyTagAndExtension_StillTimestamped()
    {
        var now = new DateTime(2026, 9, 9, 14, 30, 0);
        var name = TimestampedFileName.Build("ACCCOM", now, "", "");
        Assert.Equal("ACCCOM_20260909_143000", name);
    }

    [Fact]
    public void Build_null_or_blank_prefix_throws()
    {
        var now = new DateTime(2026, 9, 9, 14, 30, 0);

        // A null/blank prefix would NRE on prefix.Length or produce a filename
        // starting with "_" — reject it at the entry point.
        Assert.Throws<ArgumentNullException>(() => TimestampedFileName.Build(null!, now));
        Assert.Throws<ArgumentException>(() => TimestampedFileName.Build("", now));
        Assert.Throws<ArgumentException>(() => TimestampedFileName.Build("   ", now));
    }
}
