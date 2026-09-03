namespace ACCcom.Core.Services;

/// <summary>A single keyboard shortcut shown in the shortcuts overview window.
/// <paramref name="DescriptionKey"/> is a LanguageManager resource key so the
/// catalog stays language-neutral and testable without a WPF dependency.</summary>
public sealed record ShortcutInfo(string Keys, string DescriptionKey);

/// <summary>One category of shortcuts. <paramref name="GroupKey"/> is also an
/// i18n key (e.g. "Shortcuts.GroupSend").</summary>
public sealed record ShortcutGroup(string GroupKey, IReadOnlyList<ShortcutInfo> Shortcuts);

/// <summary>
/// Central registry of every keyboard shortcut the app supports. This is a
/// documentation surface (shown in the F1 shortcuts window), not the actual
/// handler — keep it in sync with MainWindow.PreviewKeyDown. It lives in Core
/// so tests can verify every key resolves in both languages.
/// </summary>
public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("Shortcuts.GroupSend",
        [
            new("Enter", "Shortcuts.Send"),
            new("Alt+1…9", "Shortcuts.SendQuickCmd"),
            new("↑/↓", "Shortcuts.HistoryNav"),
            new("Ctrl+Shift+H", "Shortcuts.ToggleHexSend"),
        ]),
        new("Shortcuts.GroupData",
        [
            new("Ctrl+C", "Shortcuts.CopySelected"),
            new("Ctrl+L", "Shortcuts.ClearRx"),
            new("Ctrl+Shift+L", "Shortcuts.ClearTx"),
            new("Ctrl+S", "Shortcuts.SaveRx"),
            new("Ctrl+Shift+S", "Shortcuts.SaveTx"),
            new("Ctrl+H", "Shortcuts.ToggleHex"),
        ]),
        new("Shortcuts.GroupNav",
        [
            new("Ctrl+F / Ctrl+1", "Shortcuts.JumpRx"),
            new("Ctrl+2", "Shortcuts.JumpTx"),
            new("F3 / Shift+F3", "Shortcuts.FindNext"),
            new("Ctrl+B", "Shortcuts.AddBookmark"),
            new("Ctrl+← / Ctrl+→", "Shortcuts.PrevNextBookmark"),
        ]),
        new("Shortcuts.GroupTools",
        [
            new("Ctrl+R", "Shortcuts.ToggleRecording"),
            new("Ctrl+Shift+K", "Shortcuts.OpenHighlights"),
            new("Ctrl+Shift+T", "Shortcuts.OpenProtocolTest"),
            new("Ctrl+Shift+E", "Shortcuts.OpenTriggers"),
        ]),
        new("Shortcuts.GroupApp",
        [
            new("Ctrl+D", "Shortcuts.ToggleTheme"),
            new("F5", "Shortcuts.RefreshPorts"),
            new("Esc", "Shortcuts.StopLoop"),
            new("F1", "Shortcuts.OpenShortcuts"),
        ]),
    ];

    /// <summary>Flattened list of every shortcut (used by the i18n completeness test).</summary>
    public static IEnumerable<ShortcutInfo> All => Groups.SelectMany(g => g.Shortcuts);
}