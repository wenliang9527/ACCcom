using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public interface ISerialService : IDisposable
{
    bool IsOpen { get; }
    string? CurrentPort { get; }
    int BaudRate { get; }
    /// <summary>The config actually applied by the current successful open, or
    /// null while no port is open — lets callers report the real port state
    /// instead of echoing the arguments of the current call.</summary>
    SerialConfig? ActiveConfig { get; }
    event Action<LogEntry>? OnDataReceived;
    event Action<string>? OnError;
    event Action? OnDisconnected;
    /// <summary>Raised once when auto-reconnect pauses because the port is not present on the system.</summary>
    event Action<string>? OnDeviceWait;
    bool Open(SerialConfig config);
    /// <summary>Async open. Default wraps the sync <see cref="Open"/> so
    /// lightweight implementations need no extra member; SerialService
    /// overrides it with the same retry policy but Task.Delay waits, so a
    /// failing open does not hold a thread-pool thread for the retry sleeps.</summary>
    Task<bool> OpenAsync(SerialConfig config) => Task.FromResult(Open(config));
    bool Send(string data, bool isHex = false);
    bool SendHex(string hex);
    bool Close();
}
