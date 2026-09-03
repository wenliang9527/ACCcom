using System.Collections.Concurrent;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ACCcom.Converters;

/// <summary>
/// Converts a hex color string ("#RRGGBB" or "#AARRGGBB") into a SolidColorBrush.
/// Returns Transparent when the input is null/empty/invalid so the caller can use
/// a DataTrigger to fall back to the default style.
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
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return TransparentBrush;

        if (Cache.TryGetValue(s, out var cached))
            return cached;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(s);
            var brush = Frozen(new SolidColorBrush(color));
            Cache[s] = brush;
            return brush;
        }
        catch
        {
            return TransparentBrush;
        }
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}