using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public interface ISerialService : IDisposable
{
    bool IsOpen { get; }
    string? CurrentPort { get; }
    int BaudRate { get; }
    event Action<LogEntry>? OnDataReceived;
    event Action<string>? OnError;
    event Action? OnDisconnected;
    /// <summary>Raised once when auto-reconnect pauses because the port is not present on the system.</summary>
    event Action<string>? OnDeviceWait;
    bool Open(SerialConfig config);
    bool Send(string data, bool isHex = false);
    bool SendHex(string hex);
    bool Close();
}
