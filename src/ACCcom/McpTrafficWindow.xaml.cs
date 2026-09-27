using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ACCcom.Core.Services;
using ACCcom.Helpers;

namespace ACCcom;

/// <summary>
/// Live view of serial traffic produced by the MCP (AI) tools. The MCP process
/// mirrors every RX/TX exchange into McpTrafficLog's shared JSONL file; this
/// window tails that file so "what the AI is sending/receiving" shows up in the
/// desktop UI even though the two processes have independent serial stacks.
/// </summary>
public partial class McpTrafficWindow : Window
{
    private sealed class TrafficRow
    {
        public int Id { get; init; }
        public string Time { get; init; } = "";
        public string Direction { get; init; } = "";
        public string Tool { get; init; } = "";
        public string Tag { get; init; } = "";
        public string Text { get; init; } = "";
        public string Hex { get; init; } = "";
        public string Payload { get; set; } = "";

        /// <summary>Tool-originated records always carry id=0 (send/close have
        /// no buffer id), and RX ids come from per-direction counters — showing
        /// a bare "#0" for tool rows read as a real cursor position. A dash
        /// marks "no id" instead.</summary>
        public string IdText => Id > 0 ? $"#{Id}" : "—";
    }

    /// <summary>Retained row cap. Also the batch size for the startup load so a
    /// pre-existing log can never grow the collection beyond it mid-load.</summary>
    private const int MaxRows = 2000;

    /// <summary>Coalescing interval for FileSystemWatcher bursts: the MCP process
    /// flushes per line, so a fast AI session raises dozens of Changed events per
    /// second. Batching keeps the UI thread to ~5 refreshes/sec.</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(200);

    private readonly ObservableCollection<TrafficRow> _allRows = new();
    private readonly ListCollectionView _filteredView;
    private readonly ObservableCollection<string> _tagOptions = new();
    private readonly HashSet<string> _knownTags = new(StringComparer.Ordinal);
    private readonly string _logPath;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounceTimer;
    private long _readOffset;
    private bool _scrolledToEnd = true;
    private bool _loadingExisting;
    private bool _paused;
    private bool _loadPending;
    private bool _newLinesInFlight;
    private string _directionFilter = ""; // "", "RX", "TX"
    private string _tagFilter = ""; // "" = all tags
    private string _searchText = "";
    private bool _hexMode;
    private DateTime _lastUpdateUtc = DateTime.MinValue;
    private bool _updatingFollow;
    private string? _flashText;
    /// <summary>Width watchers for the payload-stretch logic; torn down in
    /// OnClosed (DependencyPropertyChangedEventHandler holds strong refs).</summary>
    private readonly List<(DependencyPropertyDescriptor Descriptor, GridViewColumn Column, EventHandler Handler)> _widthWatchers = new();

    // Running RX/TX tallies maintained on add/remove: UpdateStatus used to
    // re-walk up to 2000 rows on every debounced batch (5×/sec) just to count
    // two numbers.
    private int _rxCount;
    private int _txCount;
    // FileInfo is a filesystem metadata syscall; the status bar only needs the
    // size at 1Hz, not once per refresh.
    private string _fileSizeText = "—";
    private DateTime _fileSizeCachedUtc = DateTime.MinValue;


    private readonly DispatcherTimer _flashTimer;
    private readonly DispatcherTimer _searchDebounceTimer;

    public McpTrafficWindow()
    {
        _logPath = McpTrafficLog.DefaultLogPath;

        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "McpTrafficWindow");
        RestoreColumnWidths();

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _flashText = null;
            UpdateStatus();
        };

        // Search re-filtering is deferred ~150ms: a full Refresh per keystroke
        // re-materialized up to 2000 containers while typing under load.
        _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _searchText = SearchBox.Text ?? "";
            RefreshRows();
        };

        // Filtered view on top of the raw collection so the direction filter,
        // the search box and the HEX toggle can re-render without touching the
        // tail buffer.
        _filteredView = (ListCollectionView)CollectionViewSource.GetDefaultView(_allRows);
        _filteredView.Filter = row => Matches((TrafficRow)row);
        TrafficList.ItemsSource = _filteredView;
        // ScrollChanged is raised by the inner ScrollViewer and bubbles; ListBox
        // doesn't expose it as a CLR event, so hook it via AddHandler.
        TrafficList.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(TrafficList_ScrollChanged));
        RebuildTagOptions();
        UpdateStatus();
        RestoreViewState();
        HookColumnWidthChanges();
        SizeChanged += (_, _) => StretchPayloadColumn();

        // Load whatever is already in the log, then watch for new lines.
        LoadExistingLines();

        _debounceTimer = new DispatcherTimer { Interval = DebounceInterval };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            if (_loadPending && !_paused)
            {
                // While a read is in flight, keep the pending flag: its
                // completion callback re-arms the load.
                if (!_newLinesInFlight)
                {
                    _loadPending = false;
                    LoadNewLines();
                }
            }
            else
            {
                _loadPending = false;
            }
        };

        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_logPath))
            {
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite
            };
            _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(RequestDebouncedLoad);
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void RequestDebouncedLoad()
    {
        if (_paused) return;
        _loadPending = true;
        // Restart the window so a burst of writes produces one refresh.
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private bool Matches(TrafficRow row)
    {
        if (_directionFilter.Length > 0 && row.Direction != _directionFilter) return false;
        if (_tagFilter.Length > 0 && row.Tag != _tagFilter) return false;
        if (_searchText.Length == 0) return true;
        // Text and Hex are matched independently of the HEX/ASCII display mode:
        // filtering against the mode-dependent Payload used to change what a
        // search finds when the toggle flipped.
        return row.Text.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Hex.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Tool.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Tag.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Time.Contains(_searchText, StringComparison.Ordinal)
            || row.Id.ToString().Contains(_searchText, StringComparison.Ordinal);
    }

    /// <summary>Rebuilds the tag dropdown: the All placeholder plus one entry per
    /// port tag seen so far. Preserves the current selection.</summary>
    private void RebuildTagOptions()
    {
        var allLabel = LanguageManager.Instance["McpTraffic.TagAll"];
        _tagOptions.Clear();
        _tagOptions.Add(allLabel);
        foreach (var tag in _knownTags.OrderBy(t => t, StringComparer.Ordinal))
            _tagOptions.Add(string.IsNullOrEmpty(tag) ? allLabel : tag);
        if (TagFilterBox.ItemsSource == null)
            TagFilterBox.ItemsSource = _tagOptions;
        TagFilterBox.SelectedIndex = string.IsNullOrEmpty(_tagFilter) ? 0 : Math.Max(0, _tagOptions.IndexOf(_tagFilter));
    }

    private void LoadExistingLines()
    {
        if (!File.Exists(_logPath)) return;
        _loadingExisting = true;
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            // Only the tail matters: a pre-existing log may hold up to rotation
            // size, and trimming an ObservableCollection from the front per line
            // is O(n²) — read the newest MaxRows lines and render them in one
            // pass instead of stalling startup on a huge backlog.
            var lines = new List<string>(MaxRows + 256);
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
                if (lines.Count > MaxRows + 256)
                    lines.RemoveRange(0, lines.Count - MaxRows);
            }
            if (lines.Count > MaxRows)
                lines.RemoveRange(0, lines.Count - MaxRows);
            _readOffset = fs.Length;
            var rows = new List<TrafficRow>(lines.Count);
            foreach (var line in lines)
            {
                if (TryBuildRow(line, out var row))
                    rows.Add(row);
            }
            AddRowsBatch(rows);
        }
        catch { /* file may be locked or deleted mid-read; skip gracefully */ }
        finally
        {
            _loadingExisting = false;
            UpdateStatus();
            if (TrafficList.Items.Count > 0 && _scrolledToEnd)
                TrafficList.ScrollIntoView(TrafficList.Items[^1]);
        }
    }

    /// <summary>Reads and parses newly appended lines on a worker thread and
    /// appends the parsed rows on the UI thread. Line reads + JSON parsing of a
    /// large burst used to run on the UI thread inside the debounce tick —
    /// fine at human speeds, a stutter source during binary floods.</summary>
    private void LoadNewLines()
    {
        if (_paused || _newLinesInFlight) return;
        var offset = _readOffset;
        _newLinesInFlight = true;
        Task.Run(() => ReadNewLines(offset))
            .ContinueWith(t =>
            {
                _newLinesInFlight = false;
                if (t is { IsFaulted: false, Result: not null } ok)
                {
                    _readOffset = ok.Result.Offset;
                    if (ok.Result.Rows.Count > 0)
                        AddRowsBatch(ok.Result.Rows, parsed: true);
                }
                else
                {
                    // Rotation deleted the file between events; drop the stale
                    // offset and re-read from the start on the next event.
                    _readOffset = 0;
                }
                // A watcher event or a pause/resume landed while the read was
                // in flight: re-arm so nothing is left behind.
                if (_loadPending && !_paused)
                {
                    _loadPending = false;
                    RequestDebouncedLoad();
                }
            }, Dispatcher);
    }

    /// <summary>Worker half of LoadNewLines: streams new lines from the file and
    /// parses them into rows. Returns null when the read failed (rotation race).</summary>
    private (long Offset, List<TrafficRow> Rows)? ReadNewLines(long offset)
    {
        try
        {
            var rows = new List<TrafficRow>(256);
            long newOffset;
            using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // Rotation truncates the file: a stale offset past EOF means the log
                // was rotated — re-read from the start.
                if (offset > fs.Length)
                    offset = 0;
                fs.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                while (reader.ReadLine() is { } line)
                {
                    if (TryBuildRow(line, out var row))
                        rows.Add(row);
                }
                newOffset = fs.Length;
            }
            return (newOffset, rows);
        }
        catch
        {
            return null;
        }
    }

    private bool TryBuildRow(string line, out TrafficRow row)
    {
        row = null!;
        if (!TrafficLogParser.TryParseLine(line, out var entry)) return false;
        row = new TrafficRow
        {
            Id = entry.Id,
            Time = entry.Time,
            Direction = entry.Direction,
            Tool = entry.Tool,
            Tag = entry.Tag,
            Text = entry.Text,
            Hex = entry.Hex,
            Payload = _hexMode ? entry.Hex : BuildPayload(entry.Text, entry.Hex)
        };
        return true;
    }

    /// <summary>Appends parsed rows to the tail buffer. Tag dropdown rebuild is
    /// deferred to after the mutations: ListCollectionView throws "cannot change
    /// or check contents during deferred Refresh" if a CollectionChanged (or a
    /// re-entrant Refresh/Count from SelectionChanged) runs inside DeferRefresh,
    /// which previously left the view stale until an explicit HEX Refresh.</summary>
    private void AddRowsBatch(List<TrafficRow> rows)
    {
        var added = 0;
        var sawNewTag = false;
        foreach (var row in rows)
        {
            _allRows.Add(row);
            TallyRow(row, +1);
            // Only record the tag here; rebuilding the ComboBox mid-batch fires
            // SelectionChanged → RefreshRows → Count/Refresh while the view may
            // still be processing the add above.
            if (_knownTags.Add(row.Tag)) sawNewTag = true;
            added++;
        }

        TrimOverflow(_allRows.Count - MaxRows);

        if (sawNewTag) RebuildTagOptions();

        if (added == 0) return;
        _lastUpdateUtc = DateTime.UtcNow;

        // During the batch startup load the per-row count/scroll work is
        // deferred to LoadExistingLines' finally block — one pass, not 2000.
        if (_loadingExisting) return;
        UpdateStatus();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    /// <summary>Drops the oldest rows past the cap. Small overflows trim per
    /// row (each RemoveAt(0) is one O(n) shift + notification); a burst past
    /// the chunk threshold rebinds the list once instead of issuing thousands
    /// of incremental removals.</summary>
    private void TrimOverflow(int overflow)
    {
        if (overflow <= 0) return;
        const int chunkedTrimThreshold = 512;
        if (overflow <= chunkedTrimThreshold)
        {
            for (var i = 0; i < overflow; i++)
            {
                TallyRow(_allRows[0], -1);
                _allRows.RemoveAt(0);
            }
            return;
        }

        var selected = TrafficList.SelectedItem as TrafficRow;
        for (var i = 0; i < overflow; i++)
            TallyRow(_allRows[i], -1);
        var keep = new List<TrafficRow>(_allRows.Count - overflow);
        for (var i = overflow; i < _allRows.Count; i++)
            keep.Add(_allRows[i]);
        TrafficList.ItemsSource = null;
        _allRows.Clear();
        foreach (var r in keep) _allRows.Add(r);
        TrafficList.ItemsSource = _filteredView;
        if (selected != null && keep.Contains(selected))
            TrafficList.SelectedItem = selected;
    }

    /// <summary>Payload text for a row under the current display mode: HEX mode
    /// shows the raw hex only; text mode shows text with the hex in brackets
    /// (or just hex when the exchange has no text of its own).</summary>
    private static string BuildPayload(string text, string hex)
    {
        if (!string.IsNullOrEmpty(text) && text != hex)
            return $"{text}  [{hex}]";
        return hex;
    }

    private void RefreshRows()
    {
        // Re-apply the filter (direction/search changed) or re-materialize the
        // payload text (HEX toggle changed), then re-sync count and tail-scroll.
        _filteredView.Refresh();
        UpdateStatus();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    private void TallyRow(TrafficRow row, int delta)
    {
        if (row.Direction == "RX") _rxCount += delta;
        else if (row.Direction == "TX") _txCount += delta;
    }

    private void UpdateStatus()
    {
        var visible = _filteredView.Count;
        RowCountText.Text = _directionFilter.Length == 0 && _tagFilter.Length == 0 && _searchText.Length == 0
            ? string.Format(LanguageManager.Instance["McpTraffic.RowsAll"], visible)
            : string.Format(LanguageManager.Instance["McpTraffic.RowsFiltered"], visible, _allRows.Count);

        if (ExportButton != null)
            ExportButton.IsEnabled = visible > 0;

        var rxTxPart = $"RX {_rxCount} · TX {_txCount} · ";

        // File size only needs 1Hz freshness — one metadata syscall per second
        // instead of one per refresh (5×/sec steady state, more during bursts).
        if ((DateTime.UtcNow - _fileSizeCachedUtc).TotalSeconds >= 1)
        {
            _fileSizeCachedUtc = DateTime.UtcNow;
            try
            {
                var info = new FileInfo(_logPath);
                _fileSizeText = info.Exists ? $"{info.Length / 1024.0:F1} KB" : "—";
            }
            catch
            {
                _fileSizeText = "—";
            }
        }
        var filePart = _fileSizeText;

        var updatedPart = _lastUpdateUtc == DateTime.MinValue
            ? LanguageManager.Instance["McpTraffic.NeverUpdated"]
            : _lastUpdateUtc.ToLocalTime().ToString("HH:mm:ss");
        var pausedPart = _paused ? $" · {LanguageManager.Instance["McpTraffic.Paused"]}" : "";
        var text = $"{rxTxPart}{filePart} · {updatedPart}{pausedPart}";
        StatusText.Text = _flashText == null ? text : $"{_flashText} · {text}";

        var statusKey = _paused ? "StatusWarningBrush" : "InkTertiaryBrush";
        if (TryFindResource(statusKey) is Brush brush)
            StatusText.Foreground = brush;
    }

    private void FlashStatus(string text)
    {
        _flashText = text;
        UpdateStatus();
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);

    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;

        // Ctrl+F: focus the filter box (skip when already typing in a text field
        // so SelectAll doesn't fight the caret).
        if (e.Key == Key.F && mods == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Esc: clear the filter when it has text (also handled inside the box;
        // this covers Esc from anywhere else in the window).
        if (e.Key == Key.Escape && mods == ModifierKeys.None && !string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchBox.Clear();
            e.Handled = true;
            return;
        }

        // Ctrl+C: copy the selected row — but leave native TextBox copy alone.
        if (e.Key == Key.C && mods == ModifierKeys.Control)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            if (TrafficList.SelectedItem is TrafficRow row)
            {
                CopyRowPayload(row);
                e.Handled = true;
            }
            return;
        }

        // Ctrl+E: export the filtered view.
        if (e.Key == Key.E && mods == ModifierKeys.Control)
        {
            Export_Click(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>Scroll position drives the follow-tail checkbox: scrolling away
    /// from the bottom unchecks it, scrolling back re-enables live tailing.
    /// Extent-only changes (rows appended below) are ignored so a live tail
    /// doesn't flicker off between the append and the follow-scroll.</summary>
    private void TrafficList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_updatingFollow) return;
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0) return;
        var atBottom = e.ExtentHeight - e.VerticalOffset - e.ViewportHeight <= 2;
        if ((FollowTail.IsChecked == true) == atBottom) return;
        _updatingFollow = true;
        try
        {
            FollowTail.IsChecked = atBottom;
            _scrolledToEnd = atBottom;
        }
        finally
        {
            _updatingFollow = false;
        }
    }

    private void TrafficList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
            CopyRowPayload(row);
    }

    /// <summary>Right-click selects the row under the cursor so the context menu
    /// acts on what the user pointed at, not the previous selection.</summary>
    private void TrafficList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TrafficList.InputHitTest(e.GetPosition(TrafficList)) is not DependencyObject hit) return;
        DependencyObject? dep = hit;
        while (dep != null && dep is not ListViewItem)
            dep = VisualTreeHelper.GetParent(dep);
        if (dep is ListViewItem item)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    private void TrafficContext_Opened(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is not TrafficRow row)
        {
            CtxFilterTag.IsEnabled = false;
            return;
        }
        CtxFilterTag.IsEnabled = !string.IsNullOrEmpty(row.Tag);
        CtxFilterTag.Header = string.Format(
            LanguageManager.Instance["McpTraffic.FilterByTagFormat"], row.Tag);
    }

    private void CtxCopyText_Click(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
            TryCopyToClipboard(row.Text);
    }

    private void CtxCopyHex_Click(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
            TryCopyToClipboard(row.Hex);
    }

    private void CtxCopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
            TryCopyToClipboard(AssembledRowText(row));
    }

    private void CtxFilterTag_Click(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is not TrafficRow row || string.IsNullOrEmpty(row.Tag)) return;
        var idx = _tagOptions.IndexOf(row.Tag);
        if (idx >= 0)
            TagFilterBox.SelectedIndex = idx;
    }

    private void CopyDetailText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailTextBox.Text))
            TryCopyToClipboard(DetailTextBox.Text);
    }

    private void CopyDetailHex_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(DetailHexBox.Text))
            TryCopyToClipboard(DetailHexBox.Text);
    }

    /// <summary>Clipboard write + brief status flash; shared by every copy path.</summary>
    private void TryCopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            FlashStatus(LanguageManager.Instance["McpTraffic.Copied"]);
        }
        catch { /* clipboard busy — best effort */ }
    }

    private static string AssembledRowText(TrafficRow row) =>
        $"[{row.Time}] {row.Direction} {row.Tool} {row.Tag} {row.Payload}";

    private string RowPayloadForCopy(TrafficRow row) =>
        _hexMode
            ? row.Hex
            : $"{row.Text}{(string.IsNullOrEmpty(row.Text) || row.Text == row.Hex ? "" : $"  [{row.Hex}]")}";

    private void HexToggle_Click(object sender, RoutedEventArgs e)
    {
        _hexMode = HexToggle.IsChecked == true;
        // Re-format each retained row's payload: HEX mode shows raw hex only,
        // text mode shows "text  [hex]". Re-renders every visible row without
        // touching the tail buffer.
        foreach (var item in _allRows)
            item.Payload = _hexMode ? item.Hex : BuildPayload(item.Text, item.Hex);
        RefreshRows();
    }

    private void DirectionFilter_Changed(object sender, RoutedEventArgs e)
    {
        // Radio buttons fire Checked during InitializeComponent, before the
        // filtered view is built; the default state (All) is already correct.
        if (_filteredView == null) return;
        _directionFilter = FilterRx.IsChecked == true ? "RX"
            : FilterTx.IsChecked == true ? "TX" : "";
        RefreshRows();
    }

    private void TagFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filteredView == null || TagFilterBox.SelectedIndex < 0) return;
        // Index 0 is the All placeholder; otherwise the item text is the tag.
        _tagFilter = TagFilterBox.SelectedIndex == 0 ? "" : TagFilterBox.SelectedItem as string ?? "";
        // The default session logs an empty tag; its dropdown entry reuses the
        // All label, so map it back to the empty filter.
        if (_tagFilter == LanguageManager.Instance["McpTraffic.TagAll"])
            _tagFilter = "";
        RefreshRows();
    }

    private void PauseToggle_Click(object sender, RoutedEventArgs e)
    {
        _paused = PauseToggle.IsChecked == true;
        if (_paused)
        {
            _debounceTimer.Stop();
            _loadPending = false;
        }
        else
        {
            // Catch up on anything appended while paused. If a read is still
            // in flight, mark it pending — its completion re-arms the load.
            if (_newLinesInFlight) _loadPending = true;
            else LoadNewLines();
        }
        // Swap the label through a live binding so a later language switch
        // still tracks the current state.
        var key = _paused ? "McpTraffic.Resume" : "McpTraffic.Pause";
        PauseToggle.SetBinding(ContentControl.ContentProperty,
            new Binding($"[{key}]") { Source = LanguageManager.Instance });
        UpdateStatus();
    }

    private void TrafficList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
        {
            DetailTextBox.Text = row.Text;
            DetailHexBox.Text = row.Hex;
            var parts = new List<string> { row.IdText, row.Time, row.Direction };
            if (!string.IsNullOrEmpty(row.Tool)) parts.Add(row.Tool);
            if (!string.IsNullOrEmpty(row.Tag)) parts.Add(row.Tag);
            DetailMetaText.Text = string.Join(" · ", parts);
            DetailHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailTextBox.Text = "";
            DetailHexBox.Text = "";
            DetailMetaText.Text = "";
            DetailHint.Visibility = Visibility.Visible;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (TrafficList.SelectedItem is TrafficRow row)
        {
            CopyRowPayload(row);
        }
        else if (!string.IsNullOrEmpty(DetailTextBox.Text) || !string.IsNullOrEmpty(DetailHexBox.Text))
        {
            TryCopyToClipboard($"{DetailTextBox.Text}  [{DetailHexBox.Text}]");
        }
    }

    private void CopyRowPayload(TrafficRow row) => TryCopyToClipboard(RowPayloadForCopy(row));

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_filteredView.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"mcp-traffic-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            if (dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ExportJson(writer);
            }
            else if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteLine("Id,Time,Direction,Tool,Tag,Text,Hex");
                foreach (TrafficRow row in _filteredView)
                    writer.WriteLine($"{row.Id},{CsvCell(row.Time)},{CsvCell(row.Direction)},{CsvCell(row.Tool)},{CsvCell(row.Tag)},{CsvCell(row.Text)},{CsvCell(row.Hex)}");
            }
            else
            {
                foreach (TrafficRow row in _filteredView)
                    writer.WriteLine($"[{row.Time}] {row.Direction} {row.Tool} {row.Tag} {row.Payload}");
            }
        }
        catch { /* surfacing export errors via dialog is overkill; skip gracefully */ }
    }

    /// <summary>Full-fidelity JSON export of the visible rows (the raw JSONL log
    /// file is the only other lossless source; CSV/TXT flatten the shape).</summary>
    private void ExportJson(StreamWriter writer)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        writer.WriteLine("[");
        var first = true;
        foreach (TrafficRow row in _filteredView)
        {
            writer.WriteLine((first ? "  " : "  ,") + System.Text.Json.JsonSerializer.Serialize(new
            {
                id = row.Id,
                time = row.Time,
                direction = row.Direction,
                tool = row.Tool,
                tag = row.Tag,
                text = row.Text,
                hex = row.Hex
            }, options));
            first = false;
        }
        writer.WriteLine("]");
    }

    private static string CsvCell(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private void SearchText_Changed(object sender, TextChangedEventArgs e)
    {
        // TextChanged can fire while InitializeComponent is wiring the control.
        if (_filteredView == null || _searchDebounceTimer == null) return;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        var text = SearchBox.Text ?? "";
        if (text.Length == 0)
        {
            // Clearing takes effect immediately — Esc/X must not lag a reset.
            _searchDebounceTimer.Stop();
            _searchText = "";
            RefreshRows();
            return;
        }
        if (text == _searchText) return;
        // Typing defers the (full) re-filter to a short debounce.
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) SearchBox.Clear();
    }

    private void FollowTail_Changed(object sender, RoutedEventArgs e)
    {
        if (TrafficList == null || _updatingFollow) return;
        _scrolledToEnd = FollowTail.IsChecked == true;
        if (_scrolledToEnd && TrafficList.Items.Count > 0)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _allRows.Clear();
        _rxCount = 0;
        _txCount = 0;
        _knownTags.Clear();
        _tagFilter = "";
        RebuildTagOptions();
        DetailTextBox.Text = "";
        DetailHexBox.Text = "";
        DetailMetaText.Text = "";
        DetailHint.Visibility = Visibility.Visible;
        try
        {
            // Truncate instead of deleting: the MCP process holds the file open
            // for append, and a delete/recreate cycle races with its handle.
            using var fs = new FileStream(_logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            fs.SetLength(0);
            _readOffset = 0;
        }
        catch { /* best effort */ }
        _lastUpdateUtc = DateTime.MinValue;
        UpdateStatus();
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private void RestoreColumnWidths()
    {
        if (TrafficList.View is not GridView view) return;
        var settings = WindowHelper.GetSettings();
        if (settings == null) return;

        var defaults = new double[view.Columns.Count];
        for (int i = 0; i < view.Columns.Count; i++)
            defaults[i] = view.Columns[i].Width;

        var resolved = TrafficColumnWidthStore.ResolveWidths(settings.McpTrafficColumnWidths, defaults);
        for (int i = 0; i < resolved.Length; i++)
            view.Columns[i].Width = resolved[i];
    }

    private void SaveColumnWidths()
    {
        var settings = WindowHelper.GetSettings();
        if (settings == null || TrafficList.View is not GridView view) return;
        if (view.Columns.Count == 0) return;

        var widths = new double[view.Columns.Count];
        for (int i = 0; i < view.Columns.Count; i++)
            widths[i] = view.Columns[i].Width;
        settings.McpTrafficColumnWidths = TrafficColumnWidthStore.CollectWidths(widths);
    }

    /// <summary>Restores the persisted view state (HEX mode, direction/tag/search
    /// filters, follow-tail). Runs after the timers exist and before the first
    /// load, so the restored filters apply to the startup batch too.</summary>
    private void RestoreViewState()
    {
        var settings = WindowHelper.GetSettings();
        if (settings == null) return;

        if (settings.McpTrafficHexMode != _hexMode)
        {
            _hexMode = settings.McpTrafficHexMode;
            HexToggle.IsChecked = _hexMode;
            foreach (var item in _allRows)
                item.Payload = _hexMode ? item.Hex : BuildPayload(item.Text, item.Hex);
        }

        _directionFilter = settings.McpTrafficDirectionFilter switch
        {
            "RX" => "RX",
            "TX" => "TX",
            _ => ""
        };
        FilterRx.IsChecked = _directionFilter == "RX";
        FilterTx.IsChecked = _directionFilter == "TX";
        FilterAll.IsChecked = _directionFilter.Length == 0;

        _tagFilter = settings.McpTrafficTagFilter ?? "";

        var search = settings.McpTrafficSearch ?? "";
        if (search.Length > 0)
        {
            SearchBox.Text = search;
            _searchText = search;
            SearchPlaceholder.Visibility = Visibility.Collapsed;
        }

        if (FollowTail.IsChecked != settings.McpTrafficFollowTail)
            FollowTail.IsChecked = settings.McpTrafficFollowTail;
    }

    /// <summary>Persists the current view state into the live settings object;
    /// the main window saves it to disk on app close.</summary>
    private void SaveViewState()
    {
        var settings = WindowHelper.GetSettings();
        if (settings == null) return;
        settings.McpTrafficHexMode = _hexMode;
        settings.McpTrafficDirectionFilter = _directionFilter;
        settings.McpTrafficTagFilter = _tagFilter;
        settings.McpTrafficSearch = _searchText;
        settings.McpTrafficFollowTail = FollowTail.IsChecked == true;
    }

    /// <summary>Payload column stretches to fill the leftover list width so the
    /// most important column is never the one clipped; other columns keep their
    /// user-dragged widths.</summary>
    private void HookColumnWidthChanges()
    {
        if (TrafficList.View is not GridView view || view.Columns.Count == 0) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
        foreach (GridViewColumn col in view.Columns)
        {
            if (col == PayloadColumn) continue;
            EventHandler handler = (_, _) => StretchPayloadColumn();
            descriptor.AddValueChanged(col, handler);
            _widthWatchers.Add((descriptor, col, handler));
        }
    }

    private void StretchPayloadColumn()
    {
        if (TrafficList.View is not GridView view || PayloadColumn == null) return;
        var others = 0d;
        foreach (GridViewColumn col in view.Columns)
            if (col != PayloadColumn)
                others += col.Width;
        var available = TrafficList.ActualWidth - others - SystemParameters.VerticalScrollBarWidth - 12;
        PayloadColumn.Width = Math.Clamp(available, 200, 900);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        // Persist the user's column layout and view state into the live
        // settings object; the main window saves it to disk on app close.
        SaveColumnWidths();
        SaveViewState();
        foreach (var (descriptor, column, handler) in _widthWatchers)
            descriptor.RemoveValueChanged(column, handler);
        _widthWatchers.Clear();
        _searchDebounceTimer.Stop();
        _debounceTimer.Stop();
        _flashTimer.Stop();
        _watcher?.Dispose();
        base.OnClosed(e);
    }
}
