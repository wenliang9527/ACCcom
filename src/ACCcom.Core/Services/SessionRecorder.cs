using System.IO;
using System.Text.Json;
using System.Threading;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SessionRecorder : BufferedFileWriter
{
    private int _recordedCount;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public bool IsRecording { get; private set; }
    public string? CurrentFile => CurrentFilePath;
    public int RecordedCount => _recordedCount;

    public bool StartRecording(string? filePath = null)
    {
        lock (SyncLock)
        {
            if (IsRecording) return false;

            if (string.IsNullOrEmpty(filePath))
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "recordings");
                Directory.CreateDirectory(dir);
                filePath = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
            }
            else
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }

            OpenWriter(filePath);
            _recordedCount = 0;
            IsRecording = true;
            return true;
        }
    }

    public bool StopRecording()
    {
        lock (SyncLock)
        {
            if (!IsRecording) return false;
            CloseWriter();
            IsRecording = false;
            return true;
        }
    }

    public void Record(LogEntry entry)
    {
        lock (SyncLock)
        {
            if (!IsRecording || Writer == null) return;

            var record = new
            {
                timestamp = entry.Timestamp.ToString("o"),
                direction = entry.Direction,
                portTag = entry.PortTag,
                rawHex = entry.RawHex,
                text = entry.Text,
                fields = entry.Fields
            };
            WriteCore(JsonSerializer.Serialize(record, _jsonOpts));
            _recordedCount++;
        }
    }

    public List<LogEntry> ReplayFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<LogEntry>();

        var entries = new List<LogEntry>();
        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var entry = new LogEntry
                {
                    Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                        ? DateTime.Parse(ts.GetString()!)
                        : DateTime.MinValue,
                    Direction = root.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "" : "",
                    PortTag = root.TryGetProperty("portTag", out var pt) ? pt.GetString() ?? "" : "",
                    RawHex = root.TryGetProperty("rawHex", out var hex) ? hex.GetString() ?? "" : "",
                    Text = root.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : ""
                };
                entries.Add(entry);
            }
            catch
            {
            }
        }
        return entries;
    }

    public async Task ReplaySessionAsync(
        string filePath,
        Action<LogEntry> onEntry,
        Action<int, int>? onProgress = null,
        double speedMultiplier = 1.0,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return;

        var entries = ReplayFile(filePath);
        if (entries.Count == 0) return;

        for (int i = 0; i < entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            onEntry(entries[i]);
            onProgress?.Invoke(i + 1, entries.Count);

            if (speedMultiplier > 0 && i < entries.Count - 1)
            {
                var delay = entries[i + 1].Timestamp - entries[i].Timestamp;
                if (delay > TimeSpan.Zero)
                {
                    var adjustedDelay = TimeSpan.FromTicks((long)(delay.Ticks / speedMultiplier));
                    if (adjustedDelay > TimeSpan.FromSeconds(5))
                        adjustedDelay = TimeSpan.FromSeconds(5);
                    await Task.Delay(adjustedDelay, ct).ConfigureAwait(false);
                }
            }

            while (IsPaused && !ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
    }

    public bool IsPaused { get; set; }

    public static string[] ListRecordings(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "recordings");
        if (!Directory.Exists(directory))
            return Array.Empty<string>();

        return Directory.GetFiles(directory, "*.jsonl")
            .OrderByDescending(f => Path.GetFileName(f))
            .ToArray();
    }
}
