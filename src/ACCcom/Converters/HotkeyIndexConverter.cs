using System.Globalization;
using System.Windows.Data;

namespace ACCcom.Converters;

/// <summary>
/// Converts an ItemsControl.AlternationIndex into an Alt+N hotkey label.
/// Returns an empty string for indexes >= 9 so only the first nine chips
/// advertise a shortcut.
/// </summary>
public class HotkeyIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i >= 0 && i < 9 ? $"Alt{i + 1}" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
