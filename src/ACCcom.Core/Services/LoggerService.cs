using System.IO;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class LoggerService : BufferedFileWriter
{
    private readonly string _logDir;
    private readonly long _maxFileSize = 5 * 1024 * 1024;

    public LoggerService()
    {
        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
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
        var fileName = $"ACCCOM_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var filePath = Path.Combine(_logDir, fileName);
        OpenWriter(filePath);
    }
}
