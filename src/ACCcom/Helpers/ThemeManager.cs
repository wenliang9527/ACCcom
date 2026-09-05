using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ACCcom.Helpers;

/// <summary>
/// Registry of available themes. Each id maps to Themes/{Id}.xaml and a
/// display-name language key "Theme.{Id}".
/// </summary>
public static class ThemeManager
{
    public static readonly IReadOnlyList<string> ThemeIds = new[]
    {
        "Light",
        "Dark",
        "MonetSunrise",   // 莫奈《日出·印象》
        "VanGoghWheat",   // 梵高《麦田群鸦》
        "KlimtKiss",      // 克里姆特《吻》
        "HokusaiWave",    // 葛饰北斋《神奈川冲浪》
        "VermeerPearl"    // 维米尔《戴珍珠耳环的少女》
    };

    public static bool Exists(string themeId)
    {
        foreach (var id in ThemeIds)
            if (id == themeId) return true;
        return false;
    }

    /// <summary>Localized display name for a theme id.</summary>
    public static string GetDisplayName(string themeId)
        => LanguageManager.Instance[$"Theme.{themeId}"];

    /// <summary>Returns the next theme id in the cycle (for Ctrl+D cycling).</summary>
    public static string NextOf(string themeId)
    {
        for (int i = 0; i < ThemeIds.Count; i++)
        {
            if (ThemeIds[i] == themeId)
                return ThemeIds[(i + 1) % ThemeIds.Count];
        }
        return ThemeIds[0];
    }

    /// <summary>Signature accent color of a theme, read from its dictionary.
    /// Used by the theme picker to render a swatch next to the theme name.</summary>
    public static Color GetAccent(string themeId)
    {
        try
        {
            if (GetDictionary(themeId)["Accent"] is Color accent) return accent;
        }
        catch
        {
            // fall through to default
        }
        return Colors.Gray;
    }

    // Theme XAML is compiled into the assembly and never changes at runtime, so
    // dictionaries can be cached statically: BuildThemeOptions otherwise parses
    // all 7 themes at startup (once per GetAccent call) and every theme switch
    // re-parses the target. A failed load is not cached so it can be retried.
    private static readonly Dictionary<string, ResourceDictionary> DictionaryCache = new();
    private static readonly object CacheLock = new();

    /// <summary>Returns the theme's ResourceDictionary, parsing it once and
    /// caching the instance. App.ApplyTheme and GetAccent both go through this,
    /// so a startup/switch parse is reused everywhere.</summary>
    public static ResourceDictionary GetDictionary(string themeId)
    {
        var fileName = FileNameOf(themeId);
        lock (CacheLock)
        {
            if (DictionaryCache.TryGetValue(fileName, out var cached))
                return cached;
        }

        var uri = $"pack://application:,,,/ACCcom;component/Themes/{fileName}.xaml";
        var dict = new ResourceDictionary { Source = new System.Uri(uri, System.UriKind.Absolute) };

        lock (CacheLock)
        {
            if (DictionaryCache.TryGetValue(fileName, out var existing))
                return existing; // another thread parsed it first; use that instance
            DictionaryCache[fileName] = dict;
            return dict;
        }
    }

    private static string FileNameOf(string themeId) => themeId switch
    {
        "Light" => "LightTheme",
        "Dark" => "DarkTheme",
        _ => themeId
    };
}

