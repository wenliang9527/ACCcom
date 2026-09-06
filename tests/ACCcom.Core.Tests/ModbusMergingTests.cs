using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusMergingTests
{
    [Fact]
    public void MergeRanges_UnderLimit_ReturnsSingle()
    {
        var ranges = ModbusUtils.MergeRanges(0, 10, 125);
        Assert.Single(ranges);
        Assert.Equal((ushort)0, ranges[0].start);
        Assert.Equal((ushort)10, ranges[0].count);
    }

    [Fact]
    public void MergeRanges_ExceedsLimit_Splits()
    {
        var ranges = ModbusUtils.MergeRanges(0, 200, 125);
        Assert.Equal(2, ranges.Count);
        Assert.Equal(125, ranges[0].count);
        Assert.Equal(75, ranges[1].count);
    }

    [Fact]
    public void MergeRanges_ExactLimit_SingleRange()
    {
        var ranges = ModbusUtils.MergeRanges(100, 125, 125);
        Assert.Single(ranges);
        Assert.Equal((ushort)100, ranges[0].start);
        Assert.Equal((ushort)125, ranges[0].count);
    }

    [Fact]
    public void MergeRanges_SingleAddress_ReturnsSingle()
    {
        var ranges = ModbusUtils.MergeRanges(100, 1, 125);
        Assert.Single(ranges);
        Assert.Equal((ushort)100, ranges[0].start);
        Assert.Equal((ushort)1, ranges[0].count);
    }

    [Fact]
    public void MergeRanges_ZeroCount_ReturnsEmpty()
    {
        var ranges = ModbusUtils.MergeRanges(0, 0, 125);

        Assert.Empty(ranges);
    }

    [Fact]
    public void MergeRanges_ZeroMaxPerRequest_DoesNotSpinForever()
    {
        // A 0 chunk size used to make the loop never advance — the fix clamps
        // it to 1 so the call completes with per-address ranges.
        var ranges = ModbusUtils.MergeRanges(10, 3, 0);

        Assert.Equal(3, ranges.Count);
        Assert.Equal((ushort)10, ranges[0].start);
        Assert.Equal((ushort)11, ranges[1].start);
        Assert.Equal((ushort)12, ranges[2].start);
        Assert.All(ranges, r => Assert.Equal((ushort)1, r.count));
    }

    [Fact]
    public void MergeRanges_MaxTotalCount_SplitsIntoExpectedChunks()
    {
        // The full ushort address space (65535 addresses) with the standard
        // 125-per-request limit: 65535 / 125 = 524 full chunks + 35 remainder.
        var ranges = ModbusUtils.MergeRanges(0, 65535, 125);

        Assert.Equal(525, ranges.Count);
        Assert.All(ranges.Take(524), r => Assert.Equal((ushort)125, r.count));
        Assert.Equal((ushort)35, ranges[^1].count);
        // The last chunk starts exactly where the previous 524 chunks ended.
        Assert.Equal((ushort)(524 * 125), ranges[^1].start);
    }

    [Fact]
    public void MergeRanges_NearAddressLimit_StaysWithinUshort()
    {
        // startAddr 0xFFFE with count 2 covers 0xFFFE/0xFFFF — a single range,
        // no ushort overflow in the start computation.
        var ranges = ModbusUtils.MergeRanges(0xFFFE, 2, 125);

        Assert.Single(ranges);
        Assert.Equal((ushort)0xFFFE, ranges[0].start);
        Assert.Equal((ushort)2, ranges[0].count);
    }
}
