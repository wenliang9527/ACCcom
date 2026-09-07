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
            // System.Timers.Timer throws for non-positive intervals; clamp so a
            // 0/negative interval behaves as "poll as fast as possible" instead
            // of surfacing an ArgumentException to the caller.
            var effectiveInterval = Math.Max(1, intervalMs);
            var initial = SafeGetPorts();
            _lastPorts = new HashSet<string>(initial, StringComparer.OrdinalIgnoreCase);
            _timer = new System.Timers.Timer(effectiveInterval) { AutoReset = true };
            _timer.Elapsed += (_, _) => Poll();
            _timer.Start();
            // Report the ports already present at startup as arrived, so a
            // monitor started with a device connected surfaces it immediately
            // instead of waiting for the first timer tick.
            if (initial.Length > 0)
                PortsChanged?.Invoke(initial.ToList(), []);
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

    // Internal for tests: runs one poll with an injected port snapshot instead
    // of the live OS list, so the arrived/removed diff logic is testable.
    // Empty snapshot = cleared baseline, so tests that don't care about the
    // live OS port list can establish a deterministic baseline.
    internal void Poll(IEnumerable<string>? injectedPorts)
    {
        List<string> arrived = new(), removed = new();
        lock (_lock)
        {
            if (_disposed) return;

            var current = injectedPorts == null
                ? new HashSet<string>(SafeGetPorts(), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(injectedPorts, StringComparer.OrdinalIgnoreCase);

            foreach (var p in current)
                if (!_lastPorts.Contains(p)) arrived.Add(p);
            foreach (var p in _lastPorts)
                if (!current.Contains(p)) removed.Add(p);

            _lastPorts = current;
        }

        if (arrived.Count > 0 || removed.Count > 0)
            PortsChanged?.Invoke(arrived, removed);
    }

    private void Poll()
    {
        Poll(SafeGetPorts());
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
