using System.IO.Ports;

namespace ACCcom.Core.Services;

/// <summary>
/// Polls the OS for serial port arrivals/removals so devices plugged in or
/// unplugged at runtime are detected without manual refresh.
/// </summary>
public class PortMonitorService : IDisposable
{
    private System.Timers.Timer? _timer;
    private HashSet<string> _lastPorts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Raised when the set of available ports changes (timer thread):
    /// item1 = arrived ports, item2 = removed ports.
    /// </summary>
    public event Action<List<string>, List<string>>? PortsChanged;

    public void Start(int intervalMs = 2000)
    {
        Stop();
        lock (_lock)
        {
            _lastPorts = new HashSet<string>(SafeGetPorts(), StringComparer.OrdinalIgnoreCase);
            _timer = new System.Timers.Timer(intervalMs) { AutoReset = true };
            _timer.Elapsed += (_, _) => Poll();
            _timer.Start();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Poll()
    {
        List<string> arrived = new(), removed = new();
        lock (_lock)
        {
            if (_disposed) return;

            var current = new HashSet<string>(SafeGetPorts(), StringComparer.OrdinalIgnoreCase);

            foreach (var p in current)
                if (!_lastPorts.Contains(p)) arrived.Add(p);
            foreach (var p in _lastPorts)
                if (!current.Contains(p)) removed.Add(p);

            _lastPorts = current;
        }

        if (arrived.Count > 0 || removed.Count > 0)
            PortsChanged?.Invoke(arrived, removed);
    }

    private static string[] SafeGetPorts()
    {
        try { return SerialPort.GetPortNames(); }
        catch { return Array.Empty<string>(); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
