using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        public string Time { get; init; } = "";
        public string Direction { get; init; } = "";
        public string Tool { get; init; } = "";
        public string Tag { get; init; } = "";
        public string Text { get; init; } = "";
        public string Hex { get; init; } = "";
        public string Payload { get; set; } = "";
    }

    /// <summary>Retained row cap. Also the batch size for the startup load so a
    /// pre-existing log can never grow the collection beyond it mid-load.</summary>
    private const int MaxRows = 2000;

    private readonly ObservableCollection<TrafficRow> _allRows = new();
    private readonly ListCollectionView _filteredView;
    private readonly string _logPath;
    private readonly FileSystemWatcher? _watcher;
    private long _readOffset;
    private bool _scrolledToEnd = true;
    private bool _loadingExisting;
    private string _directionFilter = ""; // "", "RX", "TX"
    private string _searchText = "";
    private bool _hexMode;

    public McpTrafficWindow()
    {
        _logPath = McpTrafficLog.DefaultLogPath;

        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "McpTrafficWindow");

        // Filtered view on top of the raw collection so the direction filter,
        // the search box and the HEX toggle can re-render without touching the
        // tail buffer.
        _filteredView = (ListCollectionView)CollectionViewSource.GetDefaultView(_allRows);
        _filteredView.Filter = row => Matches((TrafficRow)row);
        TrafficList.ItemsSource = _filteredView;
        UpdateRowCount();

        // Load whatever is already in the log, then watch for new lines.
        LoadExistingLines();

        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_logPath))
            {
                NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite
            };
            _watcher.Changed += (_, _) => Dispatcher.BeginInvoke(LoadNewLines);
            _watcher.EnableRaisingEvents = true;
        }
    }

    private bool Matches(TrafficRow row)
    {
        if (_directionFilter.Length > 0 && row.Direction != _directionFilter) return false;
        if (_searchText.Length == 0) return true;
        return row.Payload.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Tool.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || row.Tag.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
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
            foreach (var line in lines)
                AppendLine(line);
        }
        catch { /* file may be locked or deleted mid-read; skip gracefully */ }
        finally
        {
            _loadingExisting = false;
            UpdateRowCount();
            if (TrafficList.Items.Count > 0 && _scrolledToEnd)
                TrafficList.ScrollIntoView(TrafficList.Items[^1]);
        }
    }

    private void LoadNewLines()
    {
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_readOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            while (reader.ReadLine() is { } line)
                AppendLine(line);
            _readOffset = fs.Length;
        }
        catch
        {
            // Rotation deletes the file between events; drop the stale offset and
            // re-read from the start on the next event.
            _readOffset = 0;
        }
    }

    private void AppendLine(string line)
    {
        if (!TrafficLogParser.TryParseLine(line, out var entry)) return;
        _allRows.Add(new TrafficRow
        {
            Time = entry.Time,
            Direction = entry.Direction,
            Tool = entry.Tool,
            Tag = entry.Tag,
            Text = entry.Text,
            Hex = entry.Hex,
            Payload = _hexMode ? entry.Hex : BuildPayload(entry.Text, entry.Hex)
        });

        // Keep the list bounded; drop oldest rows past MaxRows.
        if (_allRows.Count > MaxRows)
            _allRows.RemoveAt(0);

        // During the batch startup load the per-row count/scroll work is
        // deferred to LoadExistingLines' finally block — one pass, not 2000.
        if (_loadingExisting) return;
        UpdateRowCount();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
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
        UpdateRowCount();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    private void UpdateRowCount()
    {
        var visible = _filteredView.Count;
        RowCountText.Text = _directionFilter.Length == 0 && _searchText.Length == 0
            ? $"{visible}"
            : $"{visible} / {_allRows.Count}";
    }

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

    private void SearchText_Changed(object sender, TextChangedEventArgs e)
    {
        // TextChanged can fire while InitializeComponent is wiring the control.
        if (_filteredView == null) return;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        _searchText = SearchBox.Text ?? "";
        RefreshRows();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) SearchBox.Clear();
    }

    private void FollowTail_Changed(object sender, RoutedEventArgs e)
    {
        if (TrafficList == null) return;
        _scrolledToEnd = FollowTail.IsChecked == true;
        if (_scrolledToEnd && TrafficList.Items.Count > 0)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _allRows.Clear();
        try { if (File.Exists(_logPath)) File.Delete(_logPath); }
        catch { /* best effort */ }
        _readOffset = 0;
        UpdateRowCount();
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(System.EventArgs e)
    {
        _watcher?.Dispose();
        base.OnClosed(e);
    }
}
