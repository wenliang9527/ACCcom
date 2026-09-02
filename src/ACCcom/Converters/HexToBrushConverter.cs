using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ACCcom.Converters;

/// <summary>Converts a hex color string ("#RRGGBB" or "#AARRGGBB") into a
/// SolidColorBrush. Returns Transparent when the input is null/empty/invalid
/// so the caller can use a DataTrigger to fall back to the default style.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return Brushes.Transparent;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(s);
            return new SolidColorBrush(c);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}