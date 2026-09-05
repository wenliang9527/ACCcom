using System.Windows;
using ACCcom.Helpers;
using ACCcom.Core.Services;

namespace ACCcom;

/// <summary>Display row for the shortcuts window: keys + resolved description.</summary>
public sealed record ShortcutDisplay(string Keys, string Description);

/// <summary>Group of shortcuts with a resolved i18n group name.</summary>
public sealed record ShortcutGroupDisplay(string GroupName, IReadOnlyList<ShortcutDisplay> Shortcuts);

public partial class ShortcutsWindow : Window
{
    public List<ShortcutGroupDisplay> ShortcutGroups { get; }

    public ShortcutsWindow()
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "ShortcutsWindow");
        ShortcutGroups = BuildGroups();
        DataContext = this;
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private static List<ShortcutGroupDisplay> BuildGroups()
    {
        return ShortcutCatalog.Groups
            .Select(g => new ShortcutGroupDisplay(
                LanguageManager.Instance[g.GroupKey],
                g.Shortcuts.Select(s => new ShortcutDisplay(s.Keys, LanguageManager.Instance[s.DescriptionKey])).ToList()))
            .ToList();
    }
}