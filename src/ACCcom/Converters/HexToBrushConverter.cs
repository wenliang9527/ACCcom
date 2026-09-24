using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ACCcom.Core.Services;

namespace ACCcom.Converters;

/// <summary>
/// Converts a hex color string ("#RRGGBB" or "#AARRGGBB") into a SolidColorBrush.
/// Null/empty/invalid input returns Transparent, or the theme's InkPrimary brush
/// when ConverterParameter="readable" so unhighlighted row text keeps a legible
/// foreground without an illegal DynamicResource TargetNullValue.
///
/// When called with ConverterParameter="readable" (text foregrounds), the color
/// is first passed through <see cref="ContrastColor.EnsureReadable"/> against
/// the active theme's surface so user-chosen highlight colors stay legible on
/// both light and dark themes. Swatches/raw uses omit the parameter and get the
/// exact stored color. The cache key includes the background and mode so a
/// theme switch never serves a brush tuned for the previous background.
///
/// Performance: DataPanel binds every visible row's foreground through this
/// converter, and rows get re-bound during virtualized scrolling. Without a
/// cache each conversion re-parses the string AND allocates a fresh brush
/// (hundreds of allocations/sec under 30ms flushes). We cache frozen brushes —
/// Frozen brushes are shareable across threads and render faster.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SolidColorBrush TransparentBrush = Frozen(Brushes.Transparent);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var readable = string.Equals(parameter as string, "readable", StringComparison.OrdinalIgnoreCase);
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            // Null HighlightColor = no highlight: readable text falls back to the
            // theme's primary ink (Binding.TargetNullValue cannot host a
            // DynamicResource — it is not a DependencyProperty target).
            if (readable && Application.Current?.TryFindResource("InkPrimaryBrush") is Brush ink)
                return ink;
            return TransparentBrush;
        }
        var bg = readable ? CurrentSurface() : Colors.Transparent;
        var cacheKey = $"{bg}|{readable}|{s}";

        if (Cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(s);
            if (readable && color.A == 255)
            {
                var (r, g, b) = ContrastColor.EnsureReadable(
                    color.R, color.G, color.B, bg.R, bg.G, bg.B);
                color = Color.FromArgb(255, r, g, b);
            }
            var brush = Frozen(new SolidColorBrush(color));
            Cache[cacheKey] = brush;
            return brush;
        }
        catch
        {
            return TransparentBrush;
        }
    }

    /// <summary>Surface color of the active theme — the background RX/TX row
    /// text actually renders on. Falls back to BgBase, then white, so a missing
    /// resource can never throw inside a binding convert pass.</summary>
    private static Color CurrentSurface()
    {
        var app = Application.Current;
        if (app == null) return Colors.White;
        if (app.TryFindResource("BgSurface") is Color surface) return surface;
        if (app.TryFindResource("BgBase") is Color baseColor) return baseColor;
        return Colors.White;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
