using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ACCcom.Core.Collections;
using ACCcom.Core.Models;

namespace ACCcom.Controls;

public partial class DataPanel : UserControl
{
    // Below this content width the split RX|TX layout cannot give both panes a
    // usable ~280px floor; force the combined view instead (Phase C).
    private const double SplitMinContentWidth = 700;

    private ScrollViewer? _rxScrollViewer;
    private ScrollViewer? _txScrollViewer;
    private ScrollViewer? _allScrollViewer;
    private bool _paneModeApplied;

    // Debounce timer for persisting field-grid column widths. The user drags a
    // header continuously; we wait for them to settle for 800ms before writing.
    private readonly DispatcherTimer _widthPersistTimer;
    private bool _widthsApplied;
    private double[]? _widthsArray;
    // Scratch buffer for the comparison pass: LayoutUpdated is a root-level
    // broadcast (fires on every window layout, including each 30ms flush), so
    // allocating a fresh double[] per event was steady gen0 noise. Only a real
    // width change copies scratch → snapshot.
    private double[] _widthsScratch = new double[0];

    public DataPanel()
    {
        InitializeComponent();
        RxListBox.Loaded += (_, _) => _rxScrollViewer = FindVisualChild<ScrollViewer>(RxListBox);
        TxListBox.Loaded += (_, _) => _txScrollViewer = FindVisualChild<ScrollViewer>(TxListBox);
        AllListBox.Loaded += (_, _) => _allScrollViewer = FindVisualChild<ScrollViewer>(AllListBox);

        // Right-click selects the entry under the cursor (Explorer behaviour):
        // a plain right-click on an unselected row collapses the selection to
        // that row; right-clicking inside an existing multi-selection keeps it
        // (with Ctrl you can extend it). Without this the context menu's
        // "Copy selected" acts on whatever was left selected last.
        RxListBox.PreviewMouseRightButtonUp += ListBox_RightClickSelect;
        TxListBox.PreviewMouseRightButtonUp += ListBox_RightClickSelect;
        AllListBox.PreviewMouseRightButtonUp += ListBox_RightClickSelect;

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ViewModels.MainViewModel oldVm)
                oldVm.PropertyChanged -= OnVmPropertyChanged;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.PropertyChanged += OnVmPropertyChanged;
            ApplyPaneMode(force: true);
        };

        _widthPersistTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        _widthPersistTimer.Tick += (_, _) =>
        {
            _widthPersistTimer.Stop();
            PersistFieldColumnWidths();
        };

        // Capture the user's width adjustments. LayoutUpdated fires for many
        // reasons (data updates, scroll changes); we filter to actual user-driven
        // width changes by snapshotting after apply and comparing on each tick.
        FieldGrid.LayoutUpdated += OnFieldGridLayoutUpdated;
        // Apply persisted column widths after the DataGrid has been measured; this
        // way DisplayIndex / ActualWidth are stable enough for our width assignment
        // to take effect instead of being ignored by the layout pass.
        FieldGrid.Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
                ApplyFieldColumnWidths(vm.GetFieldGridColumnWidths() as Dictionary<int, double>);
        };
    }

    public ListBox RxListBoxControl => RxListBox;
    public ListBox TxListBoxControl => TxListBox;
    public ListBox AllListBoxControl => AllListBox;
    public TextBox RxSearchBoxControl => RxSearchBox;
    public TextBox TxSearchBoxControl => TxSearchBox;
    public TextBox AllSearchBoxControl => AllSearchBox;

    /// <summary>True when the combined list is the active layout (user preference
    /// AND enough horizontal room). Used by MainWindow for scroll/copy/F3 routing.</summary>
    public bool IsCombinedActive =>
        CombinedPane.Visibility == Visibility.Visible && SplitPane.Visibility == Visibility.Collapsed;

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyPaneMode();

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.SplitDataPanes)
            || e.PropertyName == nameof(ViewModels.MainViewModel.DataPaneSplitRatio))
            ApplyPaneMode(force: true);
    }

    /// <summary>Shows combined vs split based on SplitDataPanes and content width.
    /// Narrow windows always use combined so neither pane falls below its MinWidth.</summary>
    private void ApplyPaneMode(bool force = false)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var wantSplit = vm.SplitDataPanes && ActualWidth >= SplitMinContentWidth;
        if (!force && _paneModeApplied && wantSplit == (SplitPane.Visibility == Visibility.Visible))
            return;
        _paneModeApplied = true;
        var flipped = wantSplit != (SplitPane.Visibility == Visibility.Visible);
        CombinedPane.Visibility = wantSplit ? Visibility.Collapsed : Visibility.Visible;
        SplitPane.Visibility = wantSplit ? Visibility.Visible : Visibility.Collapsed;
        if (wantSplit) ApplySplitRatio();
        // Narrow-window force-combined (and the reverse) can reveal a pane whose
        // filter was last settled while it was hidden — rebuild both sides.
        if (flipped) vm.DataFlow.NotifyPaneModeChanged();
    }

    private void ApplySplitRatio()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var ratio = Math.Clamp(vm.DataPaneSplitRatio, 0.1, 0.9);
        SplitRxColumn.Width = new GridLength(ratio, GridUnitType.Star);
        SplitTxColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void PaneSplitter_DragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var total = SplitRxColumn.ActualWidth + SplitTxColumn.ActualWidth;
        if (total > 1)
            vm.DataPaneSplitRatio = SplitRxColumn.ActualWidth / total;
    }

    /// <summary>Shared watermark driver for the three search boxes: the overlay
    /// TextBlock (named via the TextBox's Tag) collapses once text exists.</summary>
    private void SearchBox_Watermark_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string hintName) return;
        if (FindName(hintName) is not TextBlock hint) return;
        hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ScrollRxToEnd()
    {
        var sv = _rxScrollViewer ??= FindVisualChild<ScrollViewer>(RxListBox);
        if (sv == null) return;
        if (RxListBox.Items.Count == 0) return;
        // Only follow when the user hasn't scrolled up to inspect history;
        // otherwise new data would yank the viewport away from them.
        if (!ScrollPendulum.ShouldAutoScroll(sv.VerticalOffset, sv.ViewportHeight, sv.ExtentHeight))
            return;
        sv.ScrollToBottom();
    }

    public void ScrollTxToEnd()
    {
        var sv = _txScrollViewer ??= FindVisualChild<ScrollViewer>(TxListBox);
        if (sv == null) return;
        if (TxListBox.Items.Count == 0) return;
        if (!ScrollPendulum.ShouldAutoScroll(sv.VerticalOffset, sv.ViewportHeight, sv.ExtentHeight))
            return;
        sv.ScrollToBottom();
    }

    public void ScrollAllToEnd()
    {
        var sv = _allScrollViewer ??= FindVisualChild<ScrollViewer>(AllListBox);
        if (sv == null) return;
        if (AllListBox.Items.Count == 0) return;
        if (!ScrollPendulum.ShouldAutoScroll(sv.VerticalOffset, sv.ViewportHeight, sv.ExtentHeight))
            return;
        sv.ScrollToBottom();
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

    private void CopyAllSelected_Click(object sender, RoutedEventArgs e)
    {
        CopySelected(AllListBox, null);
    }

    /// <summary>Copies the currently selected RX entries (keyboard shortcut entry point).</summary>
    public void CopyRxSelected() => CopySelected(RxListBox, "RX");

    /// <summary>Copies the currently selected TX entries (keyboard shortcut entry point).</summary>
    public void CopyTxSelected() => CopySelected(TxListBox, "TX");

    /// <summary>Copies the currently selected combined-view entries (keyboard shortcut entry point).</summary>
    public void CopyAllSelected() => CopySelected(AllListBox, null);

    /// <summary>Explorer-style right-click selection: right-clicking an
    /// unselected row makes it the sole selection; right-clicking a row that is
    /// already part of a multi-selection leaves the selection alone (so the
    /// context menu acts on the block the user built). Ctrl extends.</summary>
    private void ListBox_RightClickSelect(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        var item = ItemFromPoint(listBox, e.GetPosition(listBox));
        if (item == null) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alreadySelected = item.IsSelected;
        if (ctrl)
        {
            item.IsSelected = !alreadySelected;
            return;
        }
        if (alreadySelected && listBox.SelectedItems.Count > 1) return;
        listBox.SelectedItems.Clear();
        item.IsSelected = true;
        item.Focus();
    }

    /// <summary>Resolves the ListBoxItem under <paramref name="point"/> (or null
    /// when the point missed every item — e.g. empty padding below the list).</summary>
    private static ListBoxItem? ItemFromPoint(ListBox listBox, Point point)
    {
        if (listBox.InputHitTest(point) is not DependencyObject hit) return null;
        for (var current = hit; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListBoxItem item) return item;
            if (ReferenceEquals(current, listBox)) break;
        }
        return null;
    }

    private static void CopySelected(ListBox listBox, string? direction)
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

    private void CopyAllEntries_Click(object sender, RoutedEventArgs e)
    {
        CopyAll(AllListBox, null);
    }

    private void CopyRxHex_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedHex(RxListBox);
    }

    private void CopyTxHex_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedHex(TxListBox);
    }

    private void CopyAllHex_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedHex(AllListBox);
    }

    /// <summary>Copies the raw hex of the selected entries (space-separated).</summary>
    private static void CopySelectedHex(ListBox listBox)
    {
        var hex = string.Join("\r\n", listBox.SelectedItems.OfType<LogEntry>()
            .Select(entry => entry.RawHex ?? "").Where(h => h.Length > 0));
        if (hex.Length > 0) CopyToClipboard(hex);
    }

    private static void CopyAll(ListBox listBox, string? direction)
    {
        if (listBox.DataContext is not ViewModels.MainViewModel vm) return;
        CopyToClipboard(vm.DataFlow.GetFormattedCopyText(listBox.Items.OfType<LogEntry>(), direction));
    }

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy in another process */ }
    }

    // ========== Field grid context menu ==========

    private void CopyFieldCell_Click(object sender, RoutedEventArgs e)
    {
        if (FieldGrid.SelectedCells.Count == 0) return;
        var cell = FieldGrid.SelectedCells[0];
        var value = cell.Column.GetCellContent(cell.Item)?.ToString() ?? "";
        if (value.Length > 0) CopyToClipboard(value);
    }

    private void CopyFieldRow_Click(object sender, RoutedEventArgs e)
    {
        var lines = FieldGrid.SelectedItems.OfType<ACCcom.Core.Models.FieldAnnotation>()
            .Select(f => $"{f.Offset}\t{f.Name}\t{f.Length}\t{f.RawHex}\t{f.DisplayValue}")
            .ToList();
        if (lines.Count > 0) CopyToClipboard(string.Join(Environment.NewLine, lines));
    }

    // ========== Field grid column-width persistence ==========

    /// <summary>Apply persisted column widths from settings. Safe to call repeatedly;
    /// the side-effect of "first apply" is the snapshot that subsequent LayoutUpdated
    /// events compare against to detect real user resizes.</summary>
    public void ApplyFieldColumnWidths(Dictionary<int, double>? saved)
    {
        if (saved == null || saved.Count == 0) return;
        for (int i = 0; i < FieldGrid.Columns.Count; i++)
        {
            if (saved.TryGetValue(i, out var width) && width > 20)
            {
                var col = FieldGrid.Columns[i];
                col.Width = new DataGridLength(Math.Max(col.MinWidth, width));
            }
        }
        _widthsApplied = true;
        _widthsArray = SnapshotWidths();
        _widthsScratch = new double[_widthsArray.Length];
    }

    private void OnFieldGridLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_widthsApplied || !FieldGrid.IsVisible) return;
        // Cheap check: did any column's width change since the last snapshot?
        var current = SnapshotWidths();
        if (_widthsArray == null || !WidthsEqual(_widthsArray, current))
        {
            var previous = _widthsArray;
            _widthsArray = current;
            // Swap buffers: the outgoing snapshot becomes the next scratch, so
            // the steady state (no resize) allocates nothing at all. A null
            // previous means current IS the scratch — allocate a fresh one to
            // avoid both references aliasing the same array.
            _widthsScratch = previous ?? new double[current.Length];
            // Defer actual persistence so we don't write to disk on every pixel of drag.
            _widthPersistTimer.Stop();
            _widthPersistTimer.Start();
        }
    }

    private double[] SnapshotWidths()
    {
        int n = FieldGrid.Columns.Count;
        var snap = _widthsScratch;
        if (snap.Length != n) snap = new double[n];
        for (int i = 0; i < n; i++)
            snap[i] = FieldGrid.Columns[i].ActualWidth;
        return snap;
    }

    private static bool WidthsEqual(double[] a, double[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - b[i]) > 0.5) return false;
        }
        return true;
    }

    private void PersistFieldColumnWidths()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        var dict = new Dictionary<int, double>();
        for (int i = 0; i < FieldGrid.Columns.Count; i++)
        {
            if (FieldGrid.Columns[i].ActualWidth > 0)
                dict[i] = Math.Round(FieldGrid.Columns[i].ActualWidth, 1);
        }
        vm.UpdateFieldGridColumnWidths(dict);
    }
}
