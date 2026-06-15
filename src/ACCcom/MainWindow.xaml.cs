using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ACCcom.Core.Services;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(new SerialService());
        DataContext = _vm;

        // Restore window position/size from settings
        var s = _vm.Settings;
        if (!double.IsNaN(s.WindowX) && !double.IsNaN(s.WindowY))
        {
            Left = s.WindowX;
            Top = s.WindowY;
        }
        if (!double.IsNaN(s.WindowWidth) && !double.IsNaN(s.WindowHeight))
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        // Apply persisted theme
        App.ApplyTheme(_vm.IsDarkTheme);

        _ = _vm.InitializeAsync();

        _vm.RxEntries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.AutoScrollRx)
                ScrollToEnd(RxListBox);
        };
        _vm.TxEntries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.AutoScrollTx)
                ScrollToEnd(TxListBox);
        };
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // History navigation (Up/Down in SendTextBox)
        if (e.Key == Key.Up && Keyboard.FocusedElement is TextBox tb && tb == SendTextBox)
        {
            _vm.NavigateHistory(-1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down && Keyboard.FocusedElement is TextBox tb2 && tb2 == SendTextBox)
        {
            _vm.NavigateHistory(1);
            e.Handled = true;
            return;
        }

        var mods = Keyboard.Modifiers;

        // Ctrl+Enter: Send data
        if (e.Key == Key.Enter && mods == ModifierKeys.Control)
        {
            if (_vm.SendCommand.CanExecute(null))
                _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
        // Enter (no Ctrl): also send
        else if (e.Key == Key.Enter && mods == ModifierKeys.None)
        {
            if (_vm.SendCommand.CanExecute(null))
                _vm.SendCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+L: Clear RX log
        else if (e.Key == Key.L && mods == ModifierKeys.Control)
        {
            _vm.ClearRxCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+L: Clear TX log
        else if (e.Key == Key.L && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.ClearTxCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+F: Focus RX search box
        else if (e.Key == Key.F && mods == ModifierKeys.Control)
        {
            RxSearchBox.Focus();
            RxSearchBox.SelectAll();
            e.Handled = true;
        }
        // Ctrl+S: Save RX log
        else if (e.Key == Key.S && mods == ModifierKeys.Control)
        {
            if (_vm.SaveRxCommand.CanExecute(null))
                _vm.SaveRxCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+S: Save TX log
        else if (e.Key == Key.S && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_vm.SaveTxCommand.CanExecute(null))
                _vm.SaveTxCommand.Execute(null);
            e.Handled = true;
        }
        // F5: Refresh ports
        else if (e.Key == Key.F5 && mods == ModifierKeys.None)
        {
            _vm.RefreshPortsCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+D: Toggle dark/light theme
        else if (e.Key == Key.D && mods == ModifierKeys.Control)
        {
            _vm.ToggleThemeCommand.Execute(null);
            e.Handled = true;
        }
        // Escape: Stop loop send
        else if (e.Key == Key.Escape && mods == ModifierKeys.None)
        {
            if (_vm.IsLooping)
                _vm.StopLoopCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+B: Add bookmark
        else if (e.Key == Key.B && mods == ModifierKeys.Control)
        {
            if (_vm.AddBookmarkCommand.CanExecute(null))
                _vm.AddBookmarkCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Right: Next bookmark
        else if (e.Key == Key.Right && mods == ModifierKeys.Control)
        {
            _vm.NextBookmarkCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Left: Previous bookmark
        else if (e.Key == Key.Left && mods == ModifierKeys.Control)
        {
            _vm.PrevBookmarkCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+H: Toggle hex display (bonus, kept from original)
        else if (e.Key == Key.H && mods == ModifierKeys.Control)
        {
            _vm.IsHexDisplayRx = !_vm.IsHexDisplayRx;
            _vm.IsHexDisplayTx = !_vm.IsHexDisplayTx;
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.SaveSettings(Left, Top, Width, Height);
        _vm.Dispose();
        base.OnClosed(e);
    }
}
