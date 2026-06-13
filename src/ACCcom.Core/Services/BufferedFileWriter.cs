using System.IO;
using System.Threading;

namespace ACCcom.Core.Services;

public abstract class BufferedFileWriter : IDisposable
{
    private StreamWriter? _writer;
    private int _pendingWrites;
    private Timer? _flushTimer;
    private bool _disposed;

    protected readonly object SyncLock = new();
    protected string? CurrentFilePath { get; private set; }

    protected void OpenWriter(string filePath, bool append = true)
    {
        lock (SyncLock)
        {
            StopTimerAndFlush();
            _writer?.Close();
            _writer?.Dispose();
            CurrentFilePath = filePath;
            _writer = new StreamWriter(filePath, append, System.Text.Encoding.UTF8);
            _pendingWrites = 0;
            _flushTimer?.Dispose();
            _flushTimer = new Timer(_ => PeriodicFlush(), null, 2000, 2000);
        }
    }

    protected void WriteCore(string line)
    {
        if (_disposed) return;
        lock (SyncLock)
        {
            if (_writer == null) return;
            _writer.WriteLine(line);
            _pendingWrites++;
            if (_pendingWrites >= 100)
            {
                _writer.Flush();
                _pendingWrites = 0;
            }
        }
    }

    protected void CloseWriter()
    {
        lock (SyncLock)
        {
            StopTimerAndFlush();
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }

    protected StreamWriter? Writer => _writer;

    private void PeriodicFlush()
    {
        lock (SyncLock)
        {
            if (_writer != null && _pendingWrites > 0)
            {
                _writer.Flush();
                _pendingWrites = 0;
            }
        }
    }

    private void StopTimerAndFlush()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_writer != null && _pendingWrites > 0)
        {
            _writer.Flush();
            _pendingWrites = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer?.Dispose();
        _flushTimer = null;
        lock (SyncLock)
        {
            if (_writer != null && _pendingWrites > 0)
            {
                _writer.Flush();
                _pendingWrites = 0;
            }
            _writer?.Close();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
