using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TrafficColumnWidthStoreTests
{
    private static readonly string[] Names = ["Id", "Time", "Direction", "Tool", "Payload"];
    private static readonly double[] Defaults = [90, 40, 110, 70, 390];

    [Fact]
    public void ResolveWidths_NoPersisted_ReturnsDefaults()
    {
        var result = TrafficColumnWidthStore.ResolveWidths(null, Names, Defaults);

        Assert.Equal(Defaults, result);
    }

    [Fact]
    public void ResolveWidths_AppliesPersistedOverDefaults()
    {
        var persisted = new Dictionary<string, double> { ["Time"] = 64, ["Payload"] = 500 };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Names, Defaults);

        Assert.Equal(90, result[0]);
        Assert.Equal(64, result[1]);
        Assert.Equal(500, result[4]);
    }

    [Fact]
    public void ResolveWidths_IgnoresNonUsableWidths()
    {
        var persisted = new Dictionary<string, double>
        {
            ["Id"] = 0,           // collapsed — ignore
            ["Time"] = -10,       // negative — ignore
            ["Direction"] = double.NaN,
            ["Tool"] = double.PositiveInfinity,
            ["Payload"] = 200     // usable
        };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Names, Defaults);

        Assert.Equal(Defaults[0], result[0]);
        Assert.Equal(Defaults[1], result[1]);
        Assert.Equal(Defaults[2], result[2]);
        Assert.Equal(Defaults[3], result[3]);
        Assert.Equal(200, result[4]);
    }

    [Fact]
    public void ResolveWidths_UnknownName_Ignored()
    {
        // A name from an older/newer layout must not shift onto the wrong
        // column — unknown keys fall back to defaults (the index-keyed scheme
        // silently remapped widths whenever a column was inserted).
        var persisted = new Dictionary<string, double> { ["Retired"] = 300 };

        var result = TrafficColumnWidthStore.ResolveWidths(persisted, Names, Defaults);

        Assert.Equal(Defaults, result);
    }

    [Fact]
    public void ResolveWidths_NameDefaultLengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TrafficColumnWidthStore.ResolveWidths(null, ["A", "B"], [90]));
    }

    [Fact]
    public void ResolveWidths_NullDefaults_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TrafficColumnWidthStore.ResolveWidths(null, Names, null!));
    }

    [Fact]
    public void CollectWidths_SkipsNonUsable()
    {
        var widths = new double[] { 90, 0, -5, 70, 390 };

        var result = TrafficColumnWidthStore.CollectWidths(Names, widths);

        Assert.Equal(3, result.Count);
        Assert.Equal(90, result["Id"]);
        Assert.Equal(70, result["Tool"]);
        Assert.Equal(390, result["Payload"]);
        Assert.False(result.ContainsKey("Time"));
        Assert.False(result.ContainsKey("Direction"));
    }

    [Fact]
    public void CollectWidths_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => TrafficColumnWidthStore.CollectWidths(Names, [90]));
    }
}
