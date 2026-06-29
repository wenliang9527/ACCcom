using System.Diagnostics;
using System.IO;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class LoggerService : BufferedFileWriter
{
    private readonly string _logDir;
    private readonly long _maxFileSize = 5 * 1024 * 1024;
    private const int MaxFileCount = 10;

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
    }

    public string CurrentLogPath => CurrentFilePath ?? "";

    public void Write(LogEntry entry)
    {
        lock (SyncLock)
        {
            if (Writer?.BaseStream.Length > _maxFileSize)
                RotateFile();

            var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
            var direction = entry.Direction;
            var line = $"[{timestamp}][{direction}] {entry.RawHex} | {entry.Text}";
            WriteCore(line);
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
}
