using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ACCcom.Core.Models;

namespace ACCcom.Converters;

public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FieldSeverity severity)
        {
            return severity switch
            {
                FieldSeverity.Warning => new SolidColorBrush(Color.FromRgb(250, 204, 21)),
                FieldSeverity.Error => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
