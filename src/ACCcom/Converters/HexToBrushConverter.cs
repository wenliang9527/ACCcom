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
    // Key = surface color (ARGB) | readable | source hex string. The surface is
    // embedded so a theme switch never serves a brush tuned for the old bg.
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SolidColorBrush TransparentBrush = Frozen(Brushes.Transparent);

    // Memoized theme resources: the null-highlight path (every unhighlighted
    // row, both bindings) and CurrentSurface used to hit TryFindResource 1-3
    // times per convert, i.e. thousands per second during virtualized scroll.
    // App.ThemeVersion bumps on each theme swap, which is the only moment these
    // can change — so the fast path is one int read.
    private static int _memoVersion = -1;
    private static Brush? _inkPrimary;
    private static Color _surface = Colors.White;

    private static void SyncThemeMemo()
    {
        var version = App.ThemeVersion;
        if (version == _memoVersion) return;
        _memoVersion = version;

        var app = Application.Current;
        _inkPrimary = app?.TryFindResource("InkPrimaryBrush") as Brush;
        if (app?.TryFindResource("BgSurface") is Color surface) _surface = surface;
        else if (app?.TryFindResource("BgBase") is Color baseColor) _surface = baseColor;
        else _surface = Colors.White;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var readable = string.Equals(parameter as string, "readable", StringComparison.OrdinalIgnoreCase);
        SyncThemeMemo();
        if (value is not string s || string.IsNullOrWhiteSpace(s))
        {
            // Null HighlightColor = no highlight: readable text falls back to the
            // theme's primary ink (Binding.TargetNullValue cannot host a
            // DynamicResource — it is not a DependencyProperty target).
            if (readable && _inkPrimary != null)
                return _inkPrimary;
            return TransparentBrush;
        }

        var bg = readable ? _surface : Colors.Transparent;
        // Interpolated string key still allocates once per convert; under row
        // recycling that is far cheaper than the old $"{bg}|..." which formatted
        // a full Color each time. Use a non-allocating composite when possible.
        var cacheKey = MakeKey(bg, readable, s);

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

    private static string MakeKey(Color bg, bool readable, string s)
        => string.Create(10 + s.Length, (bg, readable, s), static (span, state) =>
        {
            // "AARRGGBB|R|..." — fixed-width ARGB, no Color.ToString/culture.
            span[0] = ToHex(state.bg.A >> 4);
            span[1] = ToHex(state.bg.A & 0xF);
            span[2] = ToHex(state.bg.R >> 4);
            span[3] = ToHex(state.bg.R & 0xF);
            span[4] = ToHex(state.bg.G >> 4);
            span[5] = ToHex(state.bg.G & 0xF);
            span[6] = ToHex(state.bg.B >> 4);
            span[7] = ToHex(state.bg.B & 0xF);
            span[8] = state.readable ? '1' : '0';
            span[9] = '|';
            state.s.AsSpan().CopyTo(span[10..]);
        });

    private static char ToHex(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
