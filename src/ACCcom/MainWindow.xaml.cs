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
        // The VM ctor runs synchronously before the first frame; its internal
        // stage timings go to trace output (see MainViewModel ctor) and the
        // total is reported here.
        var vmSw = System.Diagnostics.Stopwatch.StartNew();
        _vm = new MainViewModel(new SerialService());
        vmSw.Stop();
        System.Diagnostics.Trace.WriteLine($"[startup] MainViewModel ctor total = {vmSw.ElapsedMilliseconds}ms");
        DataContext = _vm;

        // Secondary windows persist their position/size through this settings
        // instance (see WindowHelper.AttachWindowState).
        WindowHelper.SetSettingsProvider(() => _vm.Settings);

        // Bind the HTTP port only after the first frame is shown: a port
        // conflict degrades to a status message instead of crashing startup,
        // and the bind no longer blocks the first frame.
        Loaded += (_, _) => _vm.StartHttpAsync();

        // When launched with --open-mcp-traffic (the MCP server does this when
        // the AI starts using serial tools), surface the shared MCP traffic log
        // window right after the first frame so the user can watch the AI's
        // sends/receives without hunting for the toolbar button.
        if (App.Args?.Contains("--open-mcp-traffic") == true)
            Loaded += (_, _) => _vm.OpenMcpTrafficWindow();

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
        // Restore maximized last so Width/Height above stay the normal bounds.
        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;

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

        _vm.DataFlow.BatchFlushed += () =>
        {
            // FlushPendingEntries appends a whole batch per 30ms tick; scrolling
            // once here instead of once per added item avoids N ScrollToBottom +
            // layout passes per frame. Combined mode scrolls the merged list;
            // split mode still respects each side's auto-follow switch and the
            // sticky-bottom check inside Scroll*ToEnd.
            if (_vm.DataFlow.SplitDataPanes)
            {
                if (_vm.AutoScrollRx)
                    DataPanelControl.ScrollRxToEnd();
                if (_vm.AutoScrollTx)
                    DataPanelControl.ScrollTxToEnd();
            }
            else if (_vm.AutoScrollAll)
            {
                DataPanelControl.ScrollAllToEnd();
            }
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

        // F2: Toggle connect/disconnect — the most repeated click in a serial
        // session. Serial and the network bridge own separate commands, so route
        // by the selected connection type.
        if (e.Key == Key.F2 && mods == ModifierKeys.None)
        {
            if (_vm.SelectedConnectionType == "Serial")
                _vm.OpenCloseCommand.Execute(null);
            else
                _vm.ConnectNetworkCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Ctrl+E: Focus the send box (E for "entry"), caret at the end so Enter
        // can immediately re-send whatever is still in the box.
        if (e.Key == Key.E && mods == ModifierKeys.Control)
        {
            SendTextBox.Focus();
            SendTextBox.CaretIndex = SendTextBox.Text?.Length ?? 0;
            e.Handled = true;
            return;
        }

        // Ctrl+A: Select every entry in the focused list. Claim the key only when
        // a ListBox has focus — TextBoxes handle Ctrl+A (select all text) and
        // must keep their default.
        if (e.Key == Key.A && mods == ModifierKeys.Control &&
            Keyboard.FocusedElement is System.Windows.Controls.ListBox focusedList)
        {
            focusedList.SelectAll();
            e.Handled = true;
            return;
        }

        // Ctrl+Q: Toggle the quick-send sidebar (same as the toolbar ☰ toggle).
        if (mods == ModifierKeys.Control && e.Key == Key.Q)
        {
            _vm.ShowQuickSendSidebar = !_vm.ShowQuickSendSidebar;
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

        // Ctrl+C: Copy selected entries (active list: combined, RX, or TX)
        if (e.Key == Key.C && mods == ModifierKeys.Control)
        {
            if (DataPanelControl.AllListBoxControl.IsKeyboardFocusWithin && DataPanelControl.AllListBoxControl.SelectedItems.Count > 0)
            {
                DataPanelControl.CopyAllSelected();
                e.Handled = true;
                return;
            }
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
        // F3 / Shift+F3: Jump to the next / previous matching entry. Combined
        // mode searches the merged list; split mode keeps the classic RX jump.
        else if (e.Key == Key.F3 && mods == ModifierKeys.None)
        {
            if (_vm.JumpToRxMatch(forward: true))
            {
                ScrollJumpIntoView();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F3 && mods == ModifierKeys.Shift)
        {
            if (_vm.JumpToRxMatch(forward: false))
            {
                ScrollJumpIntoView();
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
        // Escape: focused search box with text → clear the filter first (the
        // common "wrong filter, nothing shows" moment); otherwise stop loop send.
        else if (e.Key == Key.Escape && mods == ModifierKeys.None)
        {
            if (TryClearFocusedSearch())
            {
                e.Handled = true;
            }
            else
            {
                if (_vm.IsLooping)
                    _vm.StopLoopCommand.Execute(null);
                e.Handled = true;
            }
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
        // Ctrl+H: Toggle hex display for the pane that is actually on screen
        // (the combined header binds IsHexDisplayAll, the split headers bind
        // Rx/Tx) — toggling the hidden pair would look like a dead key.
        else if (e.Key == Key.H && mods == ModifierKeys.Control)
        {
            _vm.DataFlow.ToggleHexDisplayCommand.Execute(DataPanelControl.IsCombinedActive);
            e.Handled = true;
        }
        // Ctrl+P: Toggle combined / split data panes (matches the toolbar toggle).
        else if (e.Key == Key.P && mods == ModifierKeys.Control)
        {
            _vm.SplitDataPanes = !_vm.SplitDataPanes;
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
        // Drive both hosts from one place so the sidebar and the collapsed rail
        // can never show at the same time (they share Grid.Column 2).
        if (!_vm.ShowQuickSendSidebar)
        {
            // Remember the dragged width before collapsing to the rail.
            if (!double.IsNaN(SidebarColumn.ActualWidth) && SidebarColumn.ActualWidth > 40)
                _vm.Settings.QuickSendSidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.Width = new GridLength(28);
            SidebarSplitter.Visibility = Visibility.Collapsed;
            QuickSendSidebarHost.Visibility = Visibility.Collapsed;
            QuickSendRail.Visibility = Visibility.Visible;
        }
        else
        {
            SidebarColumn.Width = new GridLength(
                Math.Clamp(_vm.Settings.QuickSendSidebarWidth > 0 ? _vm.Settings.QuickSendSidebarWidth : 260, 180, 420));
            SidebarSplitter.Visibility = Visibility.Visible;
            QuickSendSidebarHost.Visibility = Visibility.Visible;
            QuickSendRail.Visibility = Visibility.Collapsed;
        }
    }

    private void QuickSendRail_Click(object sender, MouseButtonEventArgs e)
        => _vm.ShowQuickSendSidebar = true;

    protected override void OnClosed(EventArgs e)
    {
        // While maximized/minimized, Left/Top/Width/Height track the restored
        // bounds differently per state; RestoreBounds is the reliable source
        // for "the window the user actually arranged".
        var restored = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (restored.IsEmpty || double.IsNaN(restored.Width))
            restored = new Rect(Left, Top, Width, Height);
        _vm.SaveSettings(restored.X, restored.Y, restored.Width, restored.Height,
            _vm.ShowQuickSendSidebar && !double.IsNaN(SidebarColumn.ActualWidth) ? SidebarColumn.ActualWidth : 0,
            maximized: WindowState == WindowState.Maximized);
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
    /// data-panel search box. Combined mode focuses the merged search; split mode
    /// keeps RX/TX. The ListBox itself is focused first to ensure the panel
    /// scrolls into view on a tiny window, then the search box takes focus with
    /// the existing text selected so the user can start typing immediately.</summary>
    private void FocusDataPanelSearch(bool rx)
    {
        if (!_vm.DataFlow.SplitDataPanes)
        {
            DataPanelControl.AllListBoxControl.Focus();
            DataPanelControl.AllSearchBoxControl.Focus();
            DataPanelControl.AllSearchBoxControl.SelectAll();
            return;
        }
        var listBox = rx ? DataPanelControl.RxListBoxControl : DataPanelControl.TxListBoxControl;
        var search = rx ? DataPanelControl.RxSearchBoxControl : DataPanelControl.TxSearchBoxControl;
        listBox.Focus();
        search.Focus();
        search.SelectAll();
    }

    /// <summary>Clears the focused RX/TX/combined search box when it has text.
    /// Returns true when it handled the key — Escape's first job while a filter
    /// is active, since a stale filter that hides everything looks like data loss.</summary>
    private bool TryClearFocusedSearch()
    {
        if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox tb) return false;
        TextBox? search = tb switch
        {
            var t when t == DataPanelControl.AllSearchBoxControl => t,
            var t when t == DataPanelControl.RxSearchBoxControl => t,
            var t when t == DataPanelControl.TxSearchBoxControl => t,
            _ => null
        };
        if (search == null || search.Text.Length == 0) return false;
        search.Clear();
        return true;
    }

    /// <summary>Scrolls the list that owns the current JumpToMatch selection
    /// (merged list when unsplit, RX list when split).</summary>
    private void ScrollJumpIntoView()
    {
        var selected = _vm.DataFlow.SelectedEntry;
        if (selected == null) return;
        if (_vm.DataFlow.SplitDataPanes)
            DataPanelControl.RxListBoxControl.ScrollIntoView(selected);
        else
            DataPanelControl.AllListBoxControl.ScrollIntoView(selected);
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
