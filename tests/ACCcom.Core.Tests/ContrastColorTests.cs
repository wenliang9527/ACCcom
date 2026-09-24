using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ContrastColorTests
{
    private static double Ratio((byte R, byte G, byte B) fg, (byte R, byte G, byte B) bg)
        => ContrastColor.ContrastRatio(
            ContrastColor.Luminance(fg.R, fg.G, fg.B),
            ContrastColor.Luminance(bg.R, bg.G, bg.B));

    [Fact]
    public void ContrastRatio_black_on_white_is_21()
    {
        var ratio = ContrastColor.ContrastRatio(
            ContrastColor.Luminance(0, 0, 0),
            ContrastColor.Luminance(255, 255, 255));
        Assert.Equal(21.0, ratio, 5);
    }

    [Fact]
    public void EnsureReadable_light_color_on_white_is_darkened_to_aa()
    {
        // The yellow highlight preset that was illegible on the light theme.
        var white = (bgR: (byte)247, bgG: (byte)248, bgB: (byte)250); // Light BgSurface
        var result = ContrastColor.EnsureReadable(254, 202, 87, white.bgR, white.bgG, white.bgB);

        Assert.True(Ratio(result, white) >= ContrastColor.DefaultMinRatio,
            $"ratio {Ratio(result, white):0.00} < {ContrastColor.DefaultMinRatio}");
        // Darkening keeps the warm hue: red channel must stay the largest.
        Assert.True(result.R > result.G && result.G > result.B,
            $"expected a darkened amber, got {result}");
    }

    [Fact]
    public void EnsureReadable_dark_color_on_dark_bg_is_lightened_to_aa()
    {
        var darkBg = (bgR: (byte)19, bgG: (byte)19, bgB: (byte)22); // Dark BgBase
        var result = ContrastColor.EnsureReadable(51, 51, 51, darkBg.bgR, darkBg.bgG, darkBg.bgB);

        Assert.True(Ratio(result, darkBg) >= ContrastColor.DefaultMinRatio,
            $"ratio {Ratio(result, darkBg):0.00} < {ContrastColor.DefaultMinRatio}");
        Assert.True(result.R > 51, "expected the color to move lighter");
    }

    [Fact]
    public void EnsureReadable_already_readable_color_is_unchanged()
    {
        // Dark ink on white already passes; compensation must not repaint it.
        var result = ContrastColor.EnsureReadable(0x16, 0x19, 0x1F, 255, 255, 255);
        Assert.Equal((0x16, 0x19, 0x1F), result);
    }

    [Fact]
    public void EnsureReadable_mid_gray_on_mid_gray_picks_a_reachable_pole()
    {
        // White text on #808080 only reaches ~3.95:1 — the darkening direction
        // (black, 5.3:1) is the only one that can satisfy AA here.
        var result = ContrastColor.EnsureReadable(128, 128, 128, 128, 128, 128);
        Assert.True(Ratio(result, (128, 128, 128)) >= ContrastColor.DefaultMinRatio,
            $"ratio {Ratio(result, (128, 128, 128)):0.00} < {ContrastColor.DefaultMinRatio}");
    }

    [Fact]
    public void EnsureReadable_returns_original_when_ratio_exactly_at_threshold_band()
    {
        // Pure white on pure black: maximal contrast, nothing to do.
        var result = ContrastColor.EnsureReadable(255, 255, 255, 0, 0, 0);
        Assert.Equal((255, 255, 255), result);
    }

    [Fact]
    public void EnsureReadable_yellow_preset_meets_aa_on_every_theme_surface()
    {
        // One representative surface color per shipped theme.
        (byte, byte, byte)[] themeSurfaces =
        {
            (0xF7, 0xF8, 0xFA), // Light
            (0x1A, 0x1A, 0x1F), // Dark
            (0xEB, 0xF0, 0xF5), // MonetSunrise (light)
            (0xF3, 0xED, 0xDC), // VanGoghWheat (light)
            (0x24, 0x20, 0x19), // KlimtKiss
            (0xF0, 0xE9, 0xD8), // HokusaiWave (light)
            (0x26, 0x2C, 0x39), // VermeerPearl
        };

        foreach (var bg in themeSurfaces)
        {
            var result = ContrastColor.EnsureReadable(254, 202, 87, bg.Item1, bg.Item2, bg.Item3);
            Assert.True(Ratio(result, bg) >= ContrastColor.DefaultMinRatio,
                $"yellow on #{bg.Item1:X2}{bg.Item2:X2}{bg.Item3:X2} only reaches {Ratio(result, bg):0.00}");
        }
    }
}
