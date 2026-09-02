using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ACCcom;

public partial class App : Application
{
    public static string[]? Args { get; private set; }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "crash.log");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Args = e.Args;
        base.OnStartup(e);
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

    /// <summary>Tracks the theme dictionary inserted by ApplyTheme so it can be
    /// swapped by reference — Source string matching is unreliable because a
    /// dictionary loaded from a relative Source may report it without the
    /// leading slash.</summary>
    private static ResourceDictionary? _activeTheme;
    private static string? _activeThemeId;

    public static void ApplyTheme(bool isDark)
        => ApplyTheme(isDark ? "Dark" : "Light");

    /// <summary>
    /// Switches the active theme dictionary. themeId must be one of
    /// ThemeManager.ThemeIds (e.g. "Dark", "MonetSunrise", "HokusaiWave").
    /// Falls back to the default theme if the dictionary cannot be loaded,
    /// so a bad persisted id can never crash startup.
    /// </summary>
    public static void ApplyTheme(string themeId)
    {
        var app = Current;
        if (app == null) return;

        // Same theme already active: nothing to swap.
        if (_activeTheme != null && _activeThemeId == themeId) return;

        var dicts = app.Resources.MergedDictionaries;

        // Purge every existing theme dictionary. Match "Themes/" anywhere so
        // both absolute pack URIs and the relative Source form used by
        // App.xaml ("Themes/LightTheme.xaml") are caught — leaving a stale
        // theme behind shadows the new one because WPF searches later-added
        // merged dictionaries first.
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            var src = dicts[i].Source?.ToString() ?? "";
            if (src.IndexOf("Themes/", StringComparison.OrdinalIgnoreCase) >= 0)
                dicts.RemoveAt(i);
        }
        _activeTheme = null;

        // Theme ids are stable public identifiers; two legacy ids map to
        // file names that differ from the id.
        var fileName = themeId switch
        {
            "Light" => "LightTheme",
            "Dark" => "DarkTheme",
            _ => themeId
        };
        var themeUri = $"pack://application:,,,/ACCcom;component/Themes/{fileName}.xaml";
        if (!System.Uri.TryCreate(themeUri, UriKind.Absolute, out var uri)) return;

        try
        {
            var loaded = new ResourceDictionary { Source = uri };
            dicts.Insert(0, loaded);
            _activeTheme = loaded;
            _activeThemeId = themeId;
        }
        catch (Exception)
        {
            // Unknown/corrupt theme id: restore the default light theme.
            _activeThemeId = null;
            if (!System.Uri.TryCreate(
                "pack://application:,,,/ACCcom;component/Themes/LightTheme.xaml",
                UriKind.Absolute, out var fallbackUri))
                return;
            try
            {
                var loaded = new ResourceDictionary { Source = fallbackUri };
                dicts.Insert(0, loaded);
                _activeTheme = loaded;
                _activeThemeId = "Light";
            }
            catch { /* even fallback failed; keep whatever is loaded */ }
        }
    }
}
