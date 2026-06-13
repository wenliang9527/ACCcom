namespace ACCcom.Core.Models;

public class SerialConfig
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1; // 0=None, 1=One, 2=Two
    public int Parity { get; set; } = 0; // 0=None, 1=Odd, 2=Even
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }
    public ReconnectSettings Reconnect { get; set; } = new();
}

public class ReconnectSettings
{
    public bool AutoReconnect { get; set; } = true;
    public int ReconnectIntervalMs { get; set; } = 3000;
    public int MaxReconnectAttempts { get; set; } = 0; // 0 = unlimited
    public double BackoffMultiplier { get; set; } = 1.0; // 1.0 = no backoff, 2.0 = double each retry
}
