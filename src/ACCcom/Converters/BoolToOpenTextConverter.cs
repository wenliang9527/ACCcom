using System.Globalization;
using System.Windows.Data;

namespace ACCcom.Converters;

public class BoolToOpenTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var lm = LanguageManager.Instance;
        return value is true ? lm["SerialPort.Close"] : lm["SerialPort.Open"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
