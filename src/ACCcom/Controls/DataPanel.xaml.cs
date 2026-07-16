using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACCcom.Core.Models;

namespace ACCcom.Controls;

public partial class DataPanel : UserControl
{
    public DataPanel()
    {
        InitializeComponent();
    }

    public ListBox RxListBoxControl => RxListBox;
    public ListBox TxListBoxControl => TxListBox;

    public void ScrollRxToEnd()
    {
        ScrollToEnd(RxListBox);
    }

    public void ScrollTxToEnd()
    {
        ScrollToEnd(TxListBox);
    }

    private static void ScrollToEnd(ListBox listBox)
    {
        if (listBox.Items.Count == 0) return;
        var sv = FindVisualChild<ScrollViewer>(listBox);
        if (sv != null) sv.ScrollToBottom();
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

    private void CopyRxAll_Click(object sender, RoutedEventArgs e)
    {
        CopyAll(RxListBox, "RX");
    }

    private void CopyTxAll_Click(object sender, RoutedEventArgs e)
    {
        CopyAll(TxListBox, "TX");
    }

    private void CopySelected(ListBox listBox, string direction)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var entries = new ObservableCollection<LogEntry>();
        foreach (var item in listBox.SelectedItems)
            if (item is LogEntry entry)
                entries.Add(entry);
        if (entries.Count > 0)
            Clipboard.SetText(vm.DataFlow.GetFormattedCopyText(entries, direction));
    }

    private void CopyAll(ListBox listBox, string direction)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var entries = new ObservableCollection<LogEntry>();
        foreach (var item in listBox.Items)
            if (item is LogEntry entry)
                entries.Add(entry);
        if (entries.Count > 0)
            Clipboard.SetText(vm.DataFlow.GetFormattedCopyText(entries, direction));
    }
}
