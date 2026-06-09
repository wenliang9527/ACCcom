using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Auto-scroll to bottom when new data arrives
        _vm.RxEntries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                ScrollToEnd(RxListBox);
        };
        _vm.TxEntries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                ScrollToEnd(TxListBox);
        };
    }

    private static void ScrollToEnd(ListBox listBox)
    {
        if (listBox.Items.Count == 0) return;

        // Find the ScrollViewer inside the ListBox
        var sv = FindVisualChild<ScrollViewer>(listBox);
        if (sv != null)
        {
            sv.ScrollToEnd();
        }
        else
        {
            // Fallback: use ScrollIntoView on last item
            listBox.ScrollIntoView(listBox.Items[^1]);
        }
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.RxEntries.Clear();
            _vm.TxEntries.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm.SaveRxCommand.CanExecute(null))
                _vm.SaveRxCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.SendText += "\r\n";
            }
            else
            {
                _vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
