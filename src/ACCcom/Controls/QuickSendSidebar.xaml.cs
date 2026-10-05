using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ACCcom.Core.Models;
using ACCcom.ViewModels;

namespace ACCcom.Controls;

/// <summary>
/// Right-hand sidebar hosting the paginated quick send commands.
/// DataContext: ShortcutViewModel.
/// </summary>
public partial class QuickSendSidebar : UserControl
{
    /// <summary>Delay before a single click sends, so a double-click can open the
    /// editor without a spurious first send.</summary>
    private const int ClickSendDelayMs = 220;

    private ShortcutViewModel? Vm => DataContext as ShortcutViewModel;
    private DispatcherTimer? _clickSendTimer;
    private ShortcutItem? _pendingClickItem;
    private bool _suppressNextClick;

    public QuickSendSidebar()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            _clickSendTimer?.Stop();
            _clickSendTimer = null;
            _pendingClickItem = null;
        };
    }

    // ===== Command row interactions =====

    private void CommandRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ShortcutItem item) return;

        // Keep ListBox selection in sync (button captures the click).
        if (CommandsList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem lbi)
            lbi.IsSelected = true;
        if (Vm != null) Vm.SelectedCommand = item;

        if (e.ClickCount >= 2)
        {
            _clickSendTimer?.Stop();
            _pendingClickItem = null;
            _suppressNextClick = true;
            Vm?.EditShortcut(item);
            e.Handled = true;
            return;
        }

        // Single click: defer send so a follow-up click can cancel it.
        _pendingClickItem = item;
        _clickSendTimer?.Stop();
        _clickSendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ClickSendDelayMs) };
        _clickSendTimer.Tick += ClickSendTimer_Tick;
        _clickSendTimer.Start();
    }

    private void ClickSendTimer_Tick(object? sender, EventArgs e)
    {
        _clickSendTimer?.Stop();
        var item = _pendingClickItem;
        _pendingClickItem = null;
        if (item != null && !_suppressNextClick)
            Vm?.SendShortcut(item);
        _suppressNextClick = false;
    }

    private static ShortcutItem? GetItem(object sender)
        => (sender as MenuItem)?.Tag as ShortcutItem;

    private void QuickSendSend_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.SendShortcut(item);
    }

    private void QuickSendLoad_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.LoadToSender(item);
    }

    private void QuickSendEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.EditShortcut(item);
    }

    private void QuickSendDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.DeleteShortcut(item);
    }

    private void QuickSendCopy_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.CopyCommand(item);
    }

    private void QuickSendToggleHex_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.ToggleIsHex(item);
    }

    private void QuickSendLoop_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.StartLoop(item);
    }

    private void QuickSendMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.MoveShortcut(item, -1);
    }

    private void QuickSendMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (GetItem(sender) is { } item) Vm?.MoveShortcut(item, 1);
    }

    private void RenamePage_Click(object sender, RoutedEventArgs e) => Vm?.RenameCurrentPage();

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Vm.FilterText = "";
    }

    /// <summary>Watermark driver: the overlay TextBlock (named via the TextBox's
    /// Tag) collapses once text exists. Same contract as DataPanel's search
    /// boxes — the filter starts empty, so without a hint it read as a blank box.</summary>
    private void FilterBox_Watermark_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string hintName) return;
        if (FindName(hintName) is not TextBlock hint) return;
        hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuickSendFilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(Vm.FilterText))
        {
            Vm.FilterText = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Vm.VisibleCommands.Count > 0)
        {
            if (Vm.SelectedCommand == null)
                Vm.SelectedCommand = Vm.VisibleCommands[0];
            CommandsList.Focus();
            if (CommandsList.ItemContainerGenerator.ContainerFromItem(Vm.SelectedCommand) is ListBoxItem lbi)
                lbi.Focus();
            e.Handled = true;
        }
    }

    private void CommandsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up && CommandsList.SelectedIndex <= 0 && Vm != null)
        {
            QuickSendFilterBox.Focus();
            e.Handled = true;
        }
    }
}
