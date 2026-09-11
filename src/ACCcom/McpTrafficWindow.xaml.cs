using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
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

    private readonly ObservableCollection<TrafficRow> _allRows = new();
    private readonly ListCollectionView _filteredView;
    private readonly string _logPath;
    private readonly FileSystemWatcher? _watcher;
    private long _readOffset;
    private bool _scrolledToEnd = true;
    private string _directionFilter = ""; // "", "RX", "TX"
    private bool _hexMode;

    public McpTrafficWindow()
    {
        _logPath = McpTrafficLog.DefaultLogPath;

        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "McpTrafficWindow");

        // Filtered view on top of the raw collection so the direction filter
        // and HEX toggle can re-render without touching the tail buffer.
        _filteredView = (ListCollectionView)CollectionViewSource.GetDefaultView(_allRows);
        _filteredView.Filter = row => _directionFilter.Length == 0
            || ((TrafficRow)row).Direction == _directionFilter;
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

    private void LoadExistingLines()
    {
        if (!File.Exists(_logPath)) return;
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            while (reader.ReadLine() is { } line)
                AppendLine(line);
            _readOffset = fs.Length;
        }
        catch { /* file may be locked or deleted mid-read; skip gracefully */ }
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
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var time = root.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? "" : "";
            if (time.Length >= 23) time = time[11..23]; // HH:mm:ss.fff
            var direction = root.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "" : "";
            var tool = root.TryGetProperty("tool", out var tl) ? tl.GetString() ?? "" : "";
            var tag = root.TryGetProperty("portTag", out var pt) ? pt.GetString() ?? "" : "";
            var rawHex = root.TryGetProperty("rawHex", out var hex) ? hex.GetString() ?? "" : "";
            var text = root.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "";

            _allRows.Add(new TrafficRow
            {
                Time = time,
                Direction = direction,
                Tool = tool,
                Tag = tag,
                Text = text,
                Hex = rawHex,
                Payload = _hexMode ? rawHex : BuildPayload(text, rawHex)
            });

            // Keep the list bounded; drop oldest rows past 2000.
            if (_allRows.Count > 2000)
                _allRows.RemoveAt(0);

            UpdateRowCount();
            if (TrafficList.Items.Count > 0 && _scrolledToEnd)
                TrafficList.ScrollIntoView(TrafficList.Items[^1]);
        }
        catch { /* malformed line — skip */ }
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
        // Re-apply the filter (direction changed) and re-materialize the payload
        // text (HEX toggle changed) by re-querying the rows.
        _filteredView.Refresh();
        UpdateRowCount();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
    }

    private void UpdateRowCount()
    {
        var visible = _filteredView.Count;
        RowCountText.Text = _directionFilter.Length == 0
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
        _filteredView.Refresh();
    }

    private void DirectionFilter_Changed(object sender, RoutedEventArgs e)
    {
        // Radio buttons fire Checked during InitializeComponent, before the
        // filtered view is built; the default state (All) is already correct.
        if (_filteredView == null) return;
        _directionFilter = FilterRx.IsChecked == true ? "RX"
            : FilterTx.IsChecked == true ? "TX" : "";
        _filteredView.Refresh();
        UpdateRowCount();
        if (TrafficList.Items.Count > 0 && _scrolledToEnd)
            TrafficList.ScrollIntoView(TrafficList.Items[^1]);
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