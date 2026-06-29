using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ACCcom;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "crash.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        var title = LanguageManager.Instance["App.CrashTitle"];
        var message = string.Format(LanguageManager.Instance["App.CrashMessage"], e.Exception.Message);
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash(e.ExceptionObject as Exception);
    }

    private static void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            var inner = ex.InnerException;
            while (inner != null)
            {
                sb.AppendLine($"--- Inner {inner.GetType().Name}: {inner.Message}");
                sb.AppendLine(inner.StackTrace ?? "");
                inner = inner.InnerException;
            }
            sb.AppendLine("---");
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch (Exception logEx)
        {
            Debug.WriteLine($"Failed to write crash log: {logEx.Message}");
        }
    }

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
