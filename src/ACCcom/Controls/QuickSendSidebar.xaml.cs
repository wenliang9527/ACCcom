using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.ViewModels;

namespace ACCcom.Controls;

/// <summary>
/// Right-hand sidebar hosting the paginated quick send commands.
/// DataContext: ShortcutViewModel.
/// </summary>
public partial class QuickSendSidebar : UserControl
{
    private ShortcutViewModel? Vm => DataContext as ShortcutViewModel;

    public QuickSendSidebar()
    {
        InitializeComponent();
    }

    // ===== Command row interactions =====

    private void CommandRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is ShortcutItem item)
        {
            Vm?.EditShortcut(item);
            e.Handled = true;
        }
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

    private void RenamePage_Click(object sender, RoutedEventArgs e) => Vm?.RenameCurrentPage();
}
