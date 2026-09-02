using System.Collections.Generic;

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
}
