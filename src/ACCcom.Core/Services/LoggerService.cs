using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Channels;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class LoggerService : BufferedFileWriter
{
    private readonly string _logDir;
    private readonly long _maxFileSize = 5 * 1024 * 1024;
    private const int MaxFileCount = 10;
    private const int RotateCheckEvery = 512; // entries between size checks
    private int _sinceSizeCheck;
    // SessionRecorder pattern: Write() only enqueues. Formatting, the
    // periodic size check (a BaseStream.Length query every RotateCheckEvery
    // writes) and rotation (directory scan + File.Delete) all run on the
    // drain task, so a fast RX feed never waits on a flush syscall or a
    // directory scan on its own thread.
    private Channel<LogEntry>? _channel;
    private Task? _drainTask;

    public LoggerService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "logs"))
    {
    }

    internal LoggerService(string logDir)
    {
        _logDir = logDir;
        Directory.CreateDirectory(_logDir);
        RotateFile();
        _channel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions
        {
            // The drain is a single background task; Write() may be called
            // from any number of threads.
            SingleReader = true,
            SingleWriter = false
        });
        _drainTask = Task.Run(DrainLoopAsync);
    }

    public string CurrentLogPath => CurrentFilePath ?? "";

    public void Write(LogEntry? entry)
    {
        if (entry == null) return;
        // Enqueue only — no lock shared with the flush path, no I/O. A
        // completed channel (after Dispose) drops silently.
        _channel?.Writer.TryWrite(entry);
    }

    private async Task DrainLoopAsync()
    {
        var channel = _channel;
        if (channel == null) return;
        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var direction = entry.Direction;
                var line = $"[{timestamp}][{direction}] {entry.RawHex} | {entry.Text}";
                WriteCore(line);
                // Size check lives here (drain task), never on the caller
                // thread. Avoids a BaseStream.Length query on the frame path
                // for every entry: check only every RotateCheckEvery writes.
                if (++_sinceSizeCheck >= RotateCheckEvery)
                {
                    _sinceSizeCheck = 0;
                    if (Writer?.BaseStream.Length > _maxFileSize)
                        RotateFile();
                }
            }
        }
        catch (Exception ex)
        {
            // Logging must never take the app down; Dispose still closes the
            // writer. Keep a diagnostic trace instead of silence.
            Debug.WriteLine($"[LoggerService] Drain failed: {ex.Message}");
        }
    }

    private void RotateFile()
    {
        CloseWriter();
        CleanupOldLogs();
        var fileName = $"ACCCOM_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var filePath = Path.Combine(_logDir, fileName);
        OpenWriter(filePath);
    }

    private void CleanupOldLogs()
    {
        try
        {
            var files = Directory.GetFiles(_logDir, "ACCCOM_*.log")
                .OrderByDescending(f => f)
                .ToArray();
            if (files.Length >= MaxFileCount)
            {
                foreach (var f in files.Skip(MaxFileCount - 1))
                    File.Delete(f);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Log cleanup error: {ex.Message}");
        }
    }

    /// <summary>Completes the queue, waits for the drain task (outside any
    /// lock so it can finish writing), then closes the writer — the same
    /// hand-off SessionRecorder uses. An override (not a hiding method) so
    /// `using` blocks and IDisposable dispatch reach it.</summary>
    public override void Dispose()
    {
        var channel = _channel;
        if (channel != null)
        {
            channel.Writer.TryComplete();
            try { _drainTask?.GetAwaiter().GetResult(); }
            catch (Exception ex) { Debug.WriteLine($"[LoggerService] Drain wait failed: {ex.Message}"); }
        }
        base.Dispose();
    }
}
