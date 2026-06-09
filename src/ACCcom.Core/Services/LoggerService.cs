using System.IO;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class LoggerService : IDisposable
{
    private readonly string _logDir;
    private StreamWriter? _writer;
    private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB
    private readonly object _lock = new();
    private string? _currentFilePath;

    public LoggerService()
    {
        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);
        RotateFile();
    }

    public string CurrentLogPath => _currentFilePath ?? "";

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            if (_writer == null) return;

            if (_writer.BaseStream.Length > _maxFileSize)
                RotateFile();

            var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
            var direction = entry.Direction;
            var line = $"[{timestamp}][{direction}] {entry.RawHex} | {entry.Text}";
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private void RotateFile()
    {
        _writer?.Close();
        _writer?.Dispose();
        var fileName = $"ACCCOM_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        _currentFilePath = Path.Combine(_logDir, fileName);
        _writer = new StreamWriter(_currentFilePath, append: true, System.Text.Encoding.UTF8);
    }

    public void Dispose()
    {
        _writer?.Close();
        _writer?.Dispose();
    }
}
