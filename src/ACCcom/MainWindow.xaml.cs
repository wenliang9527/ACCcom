using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ACCcom.Core.Services;
using ACCcom.Controls;
using ACCcom.Helpers;
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

        // Setup chromeless titlebar
        WindowHelper.SetupTitleBar(this, TitleBar);

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

        // Theme is applied inside MainViewModel's constructor from persisted settings;
        // no re-apply here to avoid overriding non-light/dark themes.

        // Restore quick send sidebar width + visibility
        SidebarColumn.Width = new GridLength(_vm.Settings.QuickSendSidebarWidth > 0 ? _vm.Settings.QuickSendSidebarWidth : 260);
        ApplySidebarVisibility();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowQuickSendSidebar))
                ApplySidebarVisibility();
        };

        _ = Task.Run(async () =>
        {
            try { await _vm.InitializeAsync().ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Initialize failed: {ex.Message}"); }
        });

        _vm.RxEntries.CollectionChanged += (_, e) =>
        {
            var action = e.Action;
            if (_vm.AutoScrollRx && (action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                || action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove))
                DataPanelControl.ScrollRxToEnd();
        };
        _vm.TxEntries.CollectionChanged += (_, e) =>
        {
            var action = e.Action;
            if (_vm.AutoScrollTx && (action == System.Collections.Specialized.NotifyCollectionChangedAction.Add
                || action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove))
                DataPanelControl.ScrollTxToEnd();
        };
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

        // Alt+1~9: Send quick command by index
        if (mods == ModifierKeys.Alt)
        {
            int idx = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                Key.D7 or Key.NumPad7 => 6,
                Key.D8 or Key.NumPad8 => 7,
                Key.D9 or Key.NumPad9 => 8,
                _ => -1
            };
            if (idx >= 0)
            {
                _vm.SendShortcutByIndex(idx);
                e.Handled = true;
                return;
            }
        }

        // Ctrl+C: Copy selected entries
        if (e.Key == Key.C && mods == ModifierKeys.Control)
        {
            if (DataPanelControl.RxListBoxControl.IsKeyboardFocusWithin && DataPanelControl.RxListBoxControl.SelectedItems.Count > 0)
            {
                DataPanelControl.CopyRxSelected();
                e.Handled = true;
                return;
            }
            if (DataPanelControl.TxListBoxControl.IsKeyboardFocusWithin && DataPanelControl.TxListBoxControl.SelectedItems.Count > 0)
            {
                DataPanelControl.CopyTxSelected();
                e.Handled = true;
                return;
            }
        }

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
            // Search box is now in DataPanel - need to expose it
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
        // Ctrl+H: Toggle hex display
        else if (e.Key == Key.H && mods == ModifierKeys.Control)
        {
            _vm.DataFlow.ToggleHexDisplayCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ApplySidebarVisibility()
    {
        if (!_vm.ShowQuickSendSidebar)
        {
            // Remember the dragged width before collapsing.
            if (!double.IsNaN(SidebarColumn.ActualWidth) && SidebarColumn.ActualWidth > 0)
                _vm.Settings.QuickSendSidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.Width = new GridLength(0);
        }
        else
        {
            SidebarColumn.Width = new GridLength(_vm.Settings.QuickSendSidebarWidth > 0 ? _vm.Settings.QuickSendSidebarWidth : 260);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.SaveSettings(Left, Top, Width, Height,
            _vm.ShowQuickSendSidebar && !double.IsNaN(SidebarColumn.ActualWidth) ? SidebarColumn.ActualWidth : 0);
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.Minimize(this);
    }

    private void TitleBarMax_Click(object sender, RoutedEventArgs e)
    {
        WindowHelper.MaximizeRestore(this);
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
