using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Shared serial-traffic bridge between the MCP server process and the WPF GUI.
/// Both processes run their own serial stack — the MCP stdio server's bytes live
/// in its own ToolContext buffer, invisible to the desktop app. This writer
/// mirrors every MCP RX/TX transfer into a shared JSONL file the GUI tails, so
/// "what the AI is sending/receiving" shows up in the desktop UI.
///
/// The line format matches SessionRecorder's (timestamp/direction/rawHex/text)
/// so the same deserializer can read both; the extra "tool" field names the MCP
/// tool that produced the exchange for the GUI's traffic view.
///
/// Hot-path design: <see cref="Record"/> runs on the serial DataReceived event
/// thread and on tool-call threads, so it only appends to the StreamWriter's
/// buffer under the lock — no file system call per line (a per-line flush cost
/// the RX thread a syscall per entry and a multi-millisecond rotation stall
/// every MaxLines lines). A background timer drains the buffer every
/// <see cref="FlushInterval"/>; Dispose and process exit drain synchronously.
/// The GUI tails the file via FileSystemWatcher + debounce, so the ≤100ms
/// visibility delay is imperceptible.
/// </summary>
public sealed partial class McpTrafficLog : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private int _lineCount;
    /// <summary>Set when a Record crosses MaxLines; the actual file rotation
    /// runs at the next flush so Move/Create never run on the caller thread.</summary>
    private bool _rotatePending;
    private readonly string _filePath;
    private readonly Timer _flushTimer;
    private volatile bool _disposed;

    /// <summary>How often buffered lines are pushed to the OS. Short enough
    /// that the GUI tail looks live, long enough to batch file system calls.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Where MCP traffic is mirrored. Fixed name so the GUI know exactly
    /// which file to tail without scanning a directory.</summary>
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "mcp-traffic.jsonl");

    /// <summary>The file this instance appends to (test seam: lets callers
    /// read back what was recorded).</summary>
    public string FilePath => _filePath;

    /// <summary>Rotate when the log exceeds this many lines so it cannot grow
    /// without bound across many MCP sessions. Settable so the GUI / power users
    /// can tune how much history is kept.</summary>
    public static int MaxLines { get; set; } = 5000;

    public McpTrafficLog(string? filePath = null)
    {
        _filePath = filePath ?? DefaultLogPath;
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        OpenWriter(_filePath);
        _flushTimer = new Timer(static state => ((McpTrafficLog)state!).FlushCore(), this,
            FlushInterval, FlushInterval);
    }

    /// <summary>Process-wide instance. The MCP server has exactly one
    /// ToolContext, and sharing one writer (serialized under the lock) avoids
    /// multiple processes/instances fighting over the same log file. Lazy&lt;&gt;
    /// makes the creation race-free, and a ProcessExit hook drains buffered
    /// lines so a normal exit does not lose the last flush interval.</summary>
    private static readonly Lazy<McpTrafficLog> _shared = new(() =>
    {
        var log = new McpTrafficLog();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { log.Dispose(); }
            catch { /* exiting process: never throw */ }
        };
        return log;
    });

    public static McpTrafficLog Shared => _shared.Value;

    /// <summary>Appends one traffic exchange. Non-null entries only; null drops
    /// silently (nothing meaningful to log). The optional <paramref name="tag"/>
    /// names the MCP multi-port session this exchange belongs to ("" = default
    /// single-port session).</summary>
    public void Record(int id, string toolName, string direction, string rawHex, string text, string tag = "")
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_writer == null) return;
            // Source-generated serialization (TrafficJsonContext): reflection
            // based Serialize of the record was the dominant per-line cost
            // after the flush syscall was removed, and it allocated per call.
            var record = new TrafficRecord
            {
                id = id,
                tool = toolName,
                timestamp = DateTime.Now.ToString("o"),
                direction = direction,
                rawHex = rawHex,
                text = text,
                portTag = tag
            };
            _writer.WriteLine(JsonSerializer.Serialize(record, TrafficJsonContext.Default.TrafficRecord));
            _lineCount++;
            // A non-positive MaxLines would rotate on every write; treat it as
            // "no rotation" rather than thrashing the file.
            if (MaxLines > 0 && _lineCount >= MaxLines)
                _rotatePending = true;
        }
    }

    /// <summary>Wire shape of one JSONL line. Property names are already in
    /// camelCase so the source generator emits byte-identical output to the
    /// old anonymous-type + CamelCase policy serialization.</summary>
    private sealed class TrafficRecord
    {
        public int id { get; set; }
        public string tool { get; set; } = "";
        public string timestamp { get; set; } = "";
        public string direction { get; set; } = "";
        public string rawHex { get; set; } = "";
        public string text { get; set; } = "";
        public string portTag { get; set; } = "";
    }

    [JsonSourceGenerationOptions]
    [JsonSerializable(typeof(TrafficRecord))]
    private partial class TrafficJsonContext : JsonSerializerContext;

    private void OpenWriter(string path)
    {
        lock (_lock)
        {
            // Shared read/write/delete so multiple processes (the MCP server,
            // the GUI tail viewer, and parallel test runs) can open the same
            // log simultaneously without an IOException. AutoFlush is OFF: the
            // timer/Dispose path drains instead (see class docs).
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
            _lineCount = CountLines(path);
        }
    }

    private static int CountLines(string path)
    {
        if (!File.Exists(path)) return 0;
        int count = 0;
        try
        {
            using var reader = new StreamReader(path);
            while (reader.ReadLine() != null) count++;
        }
        catch { /* best effort — treat unreadable as empty */ }
        return count;
    }

    /// <summary>Timer callback: pushes buffered lines to the OS and performs a
    /// pending rotation. Never throws — a broken pipe/disk-full must not take
    /// down the timer thread (the RX path is already decoupled from this).</summary>
    private void FlushCore()
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_writer == null) return;
            try
            {
                // The GUI's Clear truncates the file behind our append handle;
                // writing at the stale offset would leave a sparse hole of torn
                // JSONL ahead of the next record. Detect the shrink and reopen
                // fresh — buffered lines die with the cleared history (at most
                // one flush interval of post-clear records is dropped with them).
                if (_writer.BaseStream.Length < _writer.BaseStream.Position)
                {
                    ReopenTruncatedLocked();
                    return;
                }
                if (_rotatePending) RotateLocked();
                else _writer.Flush();
            }
            catch { /* rotation/flush failure is non-fatal; next tick retries */ }
        }
    }

    /// <summary>Reopens the log fresh after the GUI truncated it externally:
    /// drops the buffered writer (with its pre-clear lines) and starts a clean
    /// empty file. Runs under _lock from a flush tick.</summary>
    private void ReopenTruncatedLocked()
    {
        _writer?.Close();
        _writer?.Dispose();
        _writer = null;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
        _lineCount = 0;
    }

    /// <summary>Renames the current log to a .1 backup and starts fresh, keeping
    /// total storage bounded. Runs under _lock from a flush tick or Dispose, so
    /// file-system work never happens on a Record caller (the RX event thread).</summary>
    private void RotateLocked()
    {
        _writer?.Flush();
        _writer?.Close();
        _writer?.Dispose();
        _writer = null;

        var path = _filePath;
        try
        {
            if (File.Exists(path + ".1"))
                File.Delete(path + ".1");
            if (File.Exists(path))
                File.Move(path, path + ".1");
        }
        catch { /* rotation failure is non-fatal — reopen fresh below */ }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
        _lineCount = 0;
        _rotatePending = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Stop future ticks; an in-flight tick serializes on _lock below.
        _flushTimer.Dispose();
        lock (_lock)
        {
            if (_writer == null) return;
            try
            {
                // Drain whatever is buffered — including a rotation that a
                // Record flagged but no timer tick had run yet (the MaxLines
                // rotation test depends on Dispose performing it).
                if (_rotatePending) RotateLocked();
                else _writer.Flush();
            }
            catch { /* best effort on the way out */ }
            _writer.Close();
            _writer.Dispose();
            _writer = null;
        }
    }
}
