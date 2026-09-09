using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
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
        public string Payload { get; init; } = "";
    }

    private readonly ObservableCollection<TrafficRow> _rows = new();
    private readonly string _logPath;
    private readonly FileSystemWatcher? _watcher;
    private long _readOffset;
    private bool _scrolledToEnd = true;

    public McpTrafficWindow()
    {
        _logPath = McpTrafficLog.DefaultLogPath;

        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "McpTrafficWindow");

        TrafficList.ItemsSource = _rows;

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
            var payload = !string.IsNullOrEmpty(text) && text != rawHex ? $"{text}  [{rawHex}]" : rawHex;

            _rows.Add(new TrafficRow { Time = time, Direction = direction, Tool = tool, Tag = tag, Payload = payload });

            // Keep the list bounded; drop oldest rows past 2000.
            if (_rows.Count > 2000)
                _rows.RemoveAt(0);

            if (TrafficList.Items.Count > 0 && _scrolledToEnd)
                TrafficList.ScrollIntoView(TrafficList.Items[^1]);
        }
        catch { /* malformed line — skip */ }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        try { if (File.Exists(_logPath)) File.Delete(_logPath); }
        catch { /* best effort */ }
        _readOffset = 0;
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(System.EventArgs e)
    {
        _watcher?.Dispose();
        base.OnClosed(e);
    }
}
