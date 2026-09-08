using System.IO;
using System.Text.Json;
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
/// </summary>
public sealed class McpTrafficLog : IDisposable
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private int _lineCount;
    private readonly string _filePath;
    private bool _disposed;

    /// <summary>Where MCP traffic is mirrored. Fixed name so the GUI know exactly
    /// which file to tail without scanning a directory.</summary>
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "mcp-traffic.jsonl");

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
    }

    /// <summary>Process-wide instance. The MCP server has exactly one
    /// ToolContext, and sharing one writer (serialized under the lock) avoids
    /// multiple processes/instances fighting over the same log file.</summary>
    private static McpTrafficLog? _shared;

    public static McpTrafficLog Shared => _shared ??= new McpTrafficLog();

    /// <summary>Appends one traffic exchange. Non-null entries only; null drops
    /// silently (nothing meaningful to log).</summary>
    public void Record(int id, string toolName, string direction, string rawHex, string text)
    {
        if (_disposed) return;
        lock (_lock)
        {
            var record = new
            {
                id,
                tool = toolName,
                timestamp = DateTime.Now.ToString("o"),
                direction,
                rawHex,
                text
            };
            WriteCore(JsonSerializer.Serialize(record, _jsonOpts));
        }
    }

    private void OpenWriter(string path)
    {
        lock (_lock)
        {
            // Shared read/write/delete so multiple processes (the MCP server,
            // the GUI tail viewer, and parallel test runs) can open the same
            // log simultaneously without an IOException.
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writer = new StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
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

    private void WriteCore(string line)
    {
        if (_writer == null) return;
        _writer.WriteLine(line);
        _writer.Flush();
        _lineCount++;
        // A non-positive MaxLines would rotate on every write; treat it as "no
        // rotation" rather than thrashing the file.
        if (MaxLines > 0 && _lineCount >= MaxLines)
            Rotate();
    }

    /// <summary>Renames the current log to a .1 backup and starts fresh, keeping
    /// total storage bounded. Called lazily once MaxLines is hit.</summary>
    private void Rotate()
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
        _writer = new StreamWriter(fs, System.Text.Encoding.UTF8) { AutoFlush = true };
        _lineCount = 0;
    }

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }
}