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
}
