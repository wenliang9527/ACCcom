using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public interface ISerialService
{
    bool IsOpen { get; }
    string? CurrentPort { get; }
    int BaudRate { get; }
    event Action<LogEntry>? OnDataReceived;
    event Action<string>? OnError;
    event Action? OnDisconnected;
    bool Open(SerialConfig config);
    bool Send(string data, bool isHex = false);
    bool SendHex(string hex);
    bool Close();
    void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000);
    void UpdateReconnectSettings(ReconnectSettings settings);
}
