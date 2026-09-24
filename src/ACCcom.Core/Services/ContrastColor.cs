namespace ACCcom.Core.Services;

/// <summary>
/// WCAG-style contrast math used to keep user-chosen colors readable on every
/// theme. <see cref="EnsureReadable"/> scales a foreground toward black (light
/// backgrounds) or white (dark backgrounds) until it reaches a minimum
/// contrast ratio, so highlight presets like #FECA57 stay legible on both the
/// light and dark theme families. Pure RGB math — no WPF dependency — so it
/// can be unit-tested from the net8.0 test projects.
/// </summary>
public static class ContrastColor
{
    /// <summary>WCAG AA minimum for normal-size body text.</summary>
    public const double DefaultMinRatio = 4.5;

    /// <summary>
    /// Returns a foreground color that meets <paramref name="minRatio"/>
    /// against the given background, or the original color when it already
    /// does. Direction is chosen so the target is always mathematically
    /// reachable: backgrounds too light to read black text on force
    /// darkening, backgrounds too dark to read white text on force lightening,
    /// and mid-grays (where white itself would fail) darken instead.
    /// </summary>
    public static (byte R, byte G, byte B) EnsureReadable(
        byte fgR, byte fgG, byte fgB,
        byte bgR, byte bgG, byte bgB,
        double minRatio = DefaultMinRatio)
    {
        var fgL = Luminance(fgR, fgG, fgB);
        var bgL = Luminance(bgR, bgG, bgB);
        if (ContrastRatio(fgL, bgL) >= minRatio)
            return (fgR, fgG, fgB);

        // With a black foreground the best achievable ratio is (bgL+0.05)/0.05;
        // pick the pole that can actually reach minRatio (they overlap only in
        // a narrow band around bgL≈0.18, where either direction works).
        var darken = (bgL + 0.05) / 0.05 >= minRatio;

        for (var t = 0.05; t < 1.0; t += 0.05)
        {
            var r = Mix(fgR, t, darken);
            var g = Mix(fgG, t, darken);
            var b = Mix(fgB, t, darken);
            if (ContrastRatio(Luminance(r, g, b), bgL) >= minRatio)
                return (r, g, b);
        }

        return darken ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255);
    }

    /// <summary>WCAG relative luminance of an sRGB color (0 = black, 1 = white).</summary>
    public static double Luminance(byte r, byte g, byte b)
        => 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

    /// <summary>WCAG contrast ratio between two luminances (1 = identical, 21 = black on white).</summary>
    public static double ContrastRatio(double luminance1, double luminance2)
    {
        var lighter = Math.Max(luminance1, luminance2);
        var darker = Math.Min(luminance1, luminance2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static byte Mix(byte channel, double t, bool towardBlack)
    {
        var mixed = towardBlack ? channel * (1.0 - t) : channel + (255 - channel) * t;
        return (byte)Math.Clamp(Math.Round(mixed), 0, 255);
    }

    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
