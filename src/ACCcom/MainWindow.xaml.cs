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
            // Auto-follow only on insertions. TrimBuffer removals (oldest entries
            // falling off the 10000 cap) shift the contents but leave the visual
            // bottom in place, so scrolling again would just fight the layout.
            if (_vm.AutoScrollRx && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                DataPanelControl.ScrollRxToEnd();
        };
        _vm.TxEntries.CollectionChanged += (_, e) =>
        {
            if (_vm.AutoScrollTx && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                DataPanelControl.ScrollTxToEnd();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // History navigation (Up/Down in SendTextBox): use the Try* variant so we can
        // place the caret at the end of the restored text in one shot, avoiding the
        // caret-jump-to-start that happens when we round-trip through the binding.
        if (Keyboard.FocusedElement is TextBox focusedTb && focusedTb == SendTextBox)
        {
            int dir = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            if (dir != 0 && _vm.TryNavigateHistory(dir, out var text, out var caret))
            {
                _vm.SendText = text ?? "";
                // Re-focus the box (in case the binding update stole focus) and put
                // the caret at the end so the user can hit Enter to re-send immediately.
                SendTextBox.Focus();
                SendTextBox.CaretIndex = caret;
                e.Handled = true;
                return;
            }
        }

        var mods = Keyboard.Modifiers;

        // F1: Shortcut reference (standard help key). Works with or without
        // modifiers so the overview is always one keystroke away.
        if (e.Key == Key.F1)
        {
            _vm.OpenShortcutsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl+1 / Ctrl+2: Jump to RX / TX panel and focus the search box. Saves the
        // user a click on the search field when they want to filter incoming data.
        if (mods == ModifierKeys.Control && (e.Key == Key.D1 || e.Key == Key.NumPad1))
        {
            FocusDataPanelSearch(rx: true);
            e.Handled = true;
            return;
        }
        if (mods == ModifierKeys.Control && (e.Key == Key.D2 || e.Key == Key.NumPad2))
        {
            FocusDataPanelSearch(rx: false);
            e.Handled = true;
            return;
        }

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

        // Enter (with or without Ctrl) sends — but ONLY when the SendTextBox has focus.
        // Other TextBoxes (filter boxes, shortcut name editor, etc.) keep their default
        // Enter behaviour so they don't accidentally trigger a send.
        if (e.Key == Key.Enter &&
            (mods == ModifierKeys.None || mods == ModifierKeys.Control) &&
            Keyboard.FocusedElement is TextBox sendFocused && sendFocused == SendTextBox)
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
        // Ctrl+Shift+H: Toggle HEX send mode. Lets users flip between ASCII and
        // hex without taking their hands off the keyboard.
        else if (e.Key == Key.H && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.DataFlow.IsHexSend = !_vm.DataFlow.IsHexSend;
            e.Handled = true;
        }
        // F3 / Shift+F3: Jump to the next / previous RX entry that matches the
        // current search filter. Mirrors the standard editor find-next behaviour.
        else if (e.Key == Key.F3 && mods == ModifierKeys.None)
        {
            if (_vm.JumpToRxMatch(forward: true))
            {
                DataPanelControl.RxListBoxControl.ScrollIntoView(_vm.DataFlow.SelectedEntry);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F3 && mods == ModifierKeys.Shift)
        {
            if (_vm.JumpToRxMatch(forward: false))
            {
                DataPanelControl.RxListBoxControl.ScrollIntoView(_vm.DataFlow.SelectedEntry);
                e.Handled = true;
            }
        }
        // Ctrl+F: Focus RX search box (same as Ctrl+1; kept as the muscle-memory
        // shortcut most people reach for first).
        else if (e.Key == Key.F && mods == ModifierKeys.Control)
        {
            FocusDataPanelSearch(rx: true);
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
        // Ctrl+R: Toggle session recording (start/stop writing RX/TX to JSONL).
        else if (e.Key == Key.R && mods == ModifierKeys.Control)
        {
            if (_vm.ToggleRecordingCommand.CanExecute(null))
                _vm.ToggleRecordingCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+K: Open highlight-rule editor (visual color rules for RX/TX).
        else if (e.Key == Key.K && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.OpenHighlightCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+T: Open protocol regression test editor.
        else if (e.Key == Key.T && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.OpenProtocolTestCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+Shift+E: Open trigger-rule editor (automated RX/TX actions).
        else if (e.Key == Key.E && mods == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _vm.OpenTriggerCommand.Execute(null);
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

    private void HistoryDropDownBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.ContextMenu is null) return;
        // Position the context menu below the button and open it.
        btn.ContextMenu.PlacementTarget = btn;
        btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        btn.ContextMenu.IsOpen = true;
    }

    /// <summary>Used by Ctrl+1 / Ctrl+2 to move keyboard focus to the appropriate
    /// data-panel search box. The ListBox itself is focused first to ensure the
    /// panel scrolls into view on a tiny window, then the search box takes focus
    /// with the existing text selected so the user can start typing immediately.</summary>
    private void FocusDataPanelSearch(bool rx)
    {
        var listBox = rx ? DataPanelControl.RxListBoxControl : DataPanelControl.TxListBoxControl;
        var search = rx ? DataPanelControl.RxSearchBoxControl : DataPanelControl.TxSearchBoxControl;
        listBox.Focus();
        search.Focus();
        search.SelectAll();
    }

    private void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is string text)
        {
            _vm.SendText = text;
            SendTextBox.Focus();
            SendTextBox.CaretIndex = text.Length;
        }
    }
}
