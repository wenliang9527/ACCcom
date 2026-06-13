using System.Globalization;
using System.Windows.Data;

namespace ACCcom.Converters;

public class BoolToThemeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "☀" : "☾"; // Sun / Moon
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
