using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ACCcom.Core.Models;

namespace ACCcom.Controls;

public partial class DataPanel : UserControl
{
    private ScrollViewer? _rxScrollViewer;
    private ScrollViewer? _txScrollViewer;

    // Debounce timer for persisting field-grid column widths. The user drags a
    // header continuously; we wait for them to settle for 800ms before writing.
    private readonly DispatcherTimer _widthPersistTimer;
    private bool _widthsApplied;
    private Dictionary<int, double>? _widthsSnapshot;

    public DataPanel()
    {
        InitializeComponent();
        RxListBox.Loaded += (_, _) => _rxScrollViewer = FindVisualChild<ScrollViewer>(RxListBox);
        TxListBox.Loaded += (_, _) => _txScrollViewer = FindVisualChild<ScrollViewer>(TxListBox);

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
    public TextBox RxSearchBoxControl => RxSearchBox;
    public TextBox TxSearchBoxControl => TxSearchBox;

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
        _widthsSnapshot = SnapshotWidths();
    }

    private Dictionary<int, double> SnapshotWidths()
    {
        var snap = new Dictionary<int, double>();
        for (int i = 0; i < FieldGrid.Columns.Count; i++)
            snap[i] = FieldGrid.Columns[i].ActualWidth;
        return snap;
    }

    private void OnFieldGridLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_widthsApplied) return;
        // Cheap check: did any column's width change since the last snapshot?
        var current = SnapshotWidths();
        if (_widthsSnapshot == null || !WidthsEqual(_widthsSnapshot, current))
        {
            _widthsSnapshot = current;
            // Defer actual persistence so we don't write to disk on every pixel of drag.
            _widthPersistTimer.Stop();
            _widthPersistTimer.Start();
        }
    }

    private static bool WidthsEqual(Dictionary<int, double> a, Dictionary<int, double> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var v2) || Math.Abs(v - v2) > 0.5) return false;
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
