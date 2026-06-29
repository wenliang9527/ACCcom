using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ACCcom.Core.Models;

namespace ACCcom.Converters;

public class SeverityToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(250, 204, 21));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(220, 38, 38));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FieldSeverity severity)
        {
            return severity switch
            {
                FieldSeverity.Warning => WarningBrush,
                FieldSeverity.Error => ErrorBrush,
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
