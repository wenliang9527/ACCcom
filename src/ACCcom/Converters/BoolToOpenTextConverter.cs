using System.Globalization;
using System.Windows.Data;

namespace ACCcom.Converters;

public class BoolToOpenTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "关闭串口" : "打开串口";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
