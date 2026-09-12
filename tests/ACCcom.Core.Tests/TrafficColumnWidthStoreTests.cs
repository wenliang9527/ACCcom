using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TrafficColumnWidthStoreTests
{
    private static readonly double[] Defaults = [90, 40, 110, 70, 390];

    [Fact]
    public void ResolveWidths_NoPersisted_ReturnsDefaults()
    {
        var result = TrafficColumnWidthStore.ResolveWidths(null, Defaults);

        Assert.Equal(Defaults, result);
    }

    [Fact]
    public void ResolveWidths_AppliesPersistedOverDefaults()
    {
        var persisted = new Dictionary<int, double> { [1] = 64, [4] = 500 };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Defaults);

        Assert.Equal(90, result[0]);
        Assert.Equal(64, result[1]);
        Assert.Equal(500, result[4]);
    }

    [Fact]
    public void ResolveWidths_IgnoresNonUsableWidths()
    {
        var persisted = new Dictionary<int, double>
        {
            [0] = 0,       // collapsed — ignore
            [1] = -10,     // negative — ignore
            [2] = double.NaN,
            [3] = double.PositiveInfinity,
            [4] = 200      // usable
        };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Defaults);

        Assert.Equal(Defaults[0], result[0]);
        Assert.Equal(Defaults[1], result[1]);
        Assert.Equal(Defaults[2], result[2]);
        Assert.Equal(Defaults[3], result[3]);
        Assert.Equal(200, result[4]);
    }

    [Fact]
    public void ResolveWidths_OutOfRangeIndex_Ignored()
    {
        var persisted = new Dictionary<int, double> { [99] = 300 };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Defaults);

        Assert.Equal(Defaults, result);
    }

    [Fact]
    public void ResolveWidths_NullDefaults_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TrafficColumnWidthStore.ResolveWidths(null, null!));
    }

    [Fact]
    public void CollectWidths_SkipsNonUsable()
    {
        var widths = new double[] { 90, 0, -5, 70, 390 };

        var result = TrafficColumnWidthStore.CollectWidths(widths);

        Assert.Equal(3, result.Count);
        Assert.Equal(90, result[0]);
        Assert.Equal(70, result[3]);
        Assert.Equal(390, result[4]);
        Assert.False(result.ContainsKey(1));
        Assert.False(result.ContainsKey(2));
    }
}
