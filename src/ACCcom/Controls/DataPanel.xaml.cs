using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACCcom.Core.Models;

namespace ACCcom.Controls;

public partial class DataPanel : UserControl
{
    private ScrollViewer? _rxScrollViewer;
    private ScrollViewer? _txScrollViewer;

    public DataPanel()
    {
        InitializeComponent();
        RxListBox.Loaded += (_, _) => _rxScrollViewer = FindVisualChild<ScrollViewer>(RxListBox);
        TxListBox.Loaded += (_, _) => _txScrollViewer = FindVisualChild<ScrollViewer>(TxListBox);
    }

    public ListBox RxListBoxControl => RxListBox;
    public ListBox TxListBoxControl => TxListBox;

    public void ScrollRxToEnd()
    {
        var sv = _rxScrollViewer ??= FindVisualChild<ScrollViewer>(RxListBox);
        if (sv == null) return;
        if (RxListBox.Items.Count > 0) sv.ScrollToBottom();
    }

    public void ScrollTxToEnd()
    {
        var sv = _txScrollViewer ??= FindVisualChild<ScrollViewer>(TxListBox);
        if (sv == null) return;
        if (TxListBox.Items.Count > 0) sv.ScrollToBottom();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private void CopyRxSelected_Click(object sender, RoutedEventArgs e)
    {
        CopySelected(RxListBox, "RX");
    }

    private void CopyTxSelected_Click(object sender, RoutedEventArgs e)
    {
        CopySelected(TxListBox, "TX");
    }

    /// <summary>Copies the currently selected RX entries (keyboard shortcut entry point).</summary>
    public void CopyRxSelected() => CopySelected(RxListBox, "RX");

    /// <summary>Copies the currently selected TX entries (keyboard shortcut entry point).</summary>
    public void CopyTxSelected() => CopySelected(TxListBox, "TX");

    private static void CopySelected(ListBox listBox, string direction)
    {
        if (listBox.DataContext is not ViewModels.MainViewModel vm) return;
        CopyToClipboard(vm.DataFlow.GetFormattedCopyText(listBox.SelectedItems.OfType<LogEntry>(), direction));
    }

    private void CopyRxAll_Click(object sender, RoutedEventArgs e)
    {
        CopyAll(RxListBox, "RX");
    }

    private void CopyTxAll_Click(object sender, RoutedEventArgs e)
    {
        CopyAll(TxListBox, "TX");
    }

    private static void CopyAll(ListBox listBox, string direction)
    {
        if (listBox.DataContext is not ViewModels.MainViewModel vm) return;
        CopyToClipboard(vm.DataFlow.GetFormattedCopyText(listBox.Items.OfType<LogEntry>(), direction));
    }

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy in another process */ }
    }
}
