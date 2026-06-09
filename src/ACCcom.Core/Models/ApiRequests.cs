namespace ACCcom.Core.Models;

public class OpenPortRequest
{
    public string Port { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;   // 0=None, 1=One, 2=Two
    public int Parity { get; set; } = 0;     // 0=None, 1=Odd, 2=Even
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
}

public class SendRequest
{
    public string Data { get; set; } = "";
    public bool IsHex { get; set; }
}

public class WaitForRequest
{
    public string Pattern { get; set; } = "";
    public int TimeoutMs { get; set; } = 5000;
    public string MatchMode { get; set; } = "contains"; // contains, regex, exact
    public bool MatchHex { get; set; }
    public string? Direction { get; set; } // null=any, "RX", "TX"
}

public class ClearRequest
{
    public string? Target { get; set; } // "rx", "tx", "all"
}

public class ActivateParserRequest
{
    public string? Name { get; set; }
}
