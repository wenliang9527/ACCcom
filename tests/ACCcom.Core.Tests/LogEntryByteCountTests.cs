using ACCcom.Core.Models;

namespace ACCcom.Core.Tests;

/// <summary>
/// <see cref="LogEntry.ByteCount"/> is filled by producers that already know
/// the payload size (SerialService at read time, FrameBuffer at assembly) so
/// consumers stop rescanning the hex string on every packet. 0 means "unknown"
/// and falls back to the rescan, which keeps entries from producers that don't
/// fill it — replay, HTTP deserialization, older persisted files — correct.
/// </summary>
public class LogEntryByteCountTests
{
    [Fact]
    public void ByteCount_defaults_to_zero_signalling_unknown()
    {
        var e = new LogEntry { Id = 1, Direction = "RX", RawHex = "AA BB" };

        Assert.Equal(0, e.ByteCount);
    }

    [Theory]
    [InlineData(0, "AA BB", 2)]      // unknown → rescan the hex string
    [InlineData(5, "AA BB", 5)]      // carried → used as-is, no rescan
    [InlineData(0, "", 0)]           // unknown + empty → 0, no exception
    [InlineData(7, "", 7)]           // carried wins even when the hex is empty
    public void ResolvedByteCount_prefers_the_carried_value(int carried, string hex, int expected)
    {
        var e = new LogEntry { Id = 1, Direction = "RX", RawHex = hex, ByteCount = carried };

        Assert.Equal(expected, e.ResolvedByteCount());
    }

    [Fact]
    public void ResolvedByteCount_falls_back_to_the_same_count_as_a_manual_rescan()
    {
        // The fallback must stay consistent with every other consumer of the
        // shared helper — a drift here would silently change byte statistics.
        var e = new LogEntry { Id = 1, Direction = "RX", RawHex = "01 03 00 0A 00 01" };

        Assert.Equal(HexHelper.CountHexBytes(e.RawHex), e.ResolvedByteCount());
        Assert.Equal(6, e.ResolvedByteCount());
    }
}
