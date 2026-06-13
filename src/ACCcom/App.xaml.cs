using System.Windows;

namespace ACCcom;

public partial class App : Application
{
    public static void ApplyTheme(bool isDark)
    {
        var app = Current;
        if (app == null) return;
        var dicts = app.Resources.MergedDictionaries;

        // Remove any existing theme dictionary
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.ToString() ?? "";
            if (src.Contains("LightTheme") || src.Contains("DarkTheme"))
            {
                dicts.RemoveAt(i);
            }
        }

        // Add the requested theme
        var themeUri = isDark
            ? "pack://application:,,,/ACCcom;component/Themes/DarkTheme.xaml"
            : "pack://application:,,,/ACCcom;component/Themes/LightTheme.xaml";

        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri(themeUri, UriKind.Absolute)
        });
    }
}
