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

public class WriteParserRequest
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}

public class ParseRawRequest
{
    public string Hex { get; set; } = "";
    public string? ParserName { get; set; }
}

// ========== Multi-Port ==========

public class MultiPortOpenRequest
{
    public string Tag { get; set; } = "";
    public string Port { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public int Parity { get; set; } = 0;
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
}

public class MultiPortSendRequest
{
    public string Tag { get; set; } = "";
    public string Data { get; set; } = "";
    public bool IsHex { get; set; }
}

public class MultiPortTagRequest
{
    public string Tag { get; set; } = "";
}

// ========== Modbus ==========

public class ModbusReadRequest
{
    public byte SlaveId { get; set; } = 1;
    public string FunctionCode { get; set; } = "ReadHoldingRegisters";
    public ushort StartAddress { get; set; } = 0;
    public ushort Quantity { get; set; } = 10;
    public int TimeoutMs { get; set; } = 1000;
    public string? ConnectionId { get; set; }
}

public class ModbusWriteRequest
{
    public byte SlaveId { get; set; } = 1;
    public string FunctionCode { get; set; } = "WriteSingleRegister";
    public ushort Address { get; set; } = 0;
    public ushort Value { get; set; } = 0;
    public string? Values { get; set; }
    public string? AndMask { get; set; }
    public string? OrMask { get; set; }
    public int TimeoutMs { get; set; } = 1000;
    public string? ConnectionId { get; set; }
}

public class ModbusScanRequest
{
    public byte StartAddress { get; set; } = 1;
    public byte EndAddress { get; set; } = 247;
    public int TimeoutMs { get; set; } = 500;
    public string? ConnectionId { get; set; }
}

public class SlaveCreateRequest
{
    public byte SlaveId { get; set; } = 1;
    public string Transport { get; set; } = "tcp";
    public string ConnectionParam { get; set; } = "15000";
    public int Coils { get; set; } = 1024;
    public int DiscreteInputs { get; set; } = 1024;
    public int HoldingRegisters { get; set; } = 256;
    public int InputRegisters { get; set; } = 256;
}

public class SlaveWriteRequest
{
    public string SlaveId { get; set; } = "";
    public string Type { get; set; } = "holding";
    public ushort Address { get; set; }
    public ushort Value { get; set; }
}

public class SlaveReadRequest
{
    public string SlaveId { get; set; } = "";
    public string Type { get; set; } = "holding";
    public ushort Address { get; set; }
}

public class BaudDetectRequest
{
    public string Port { get; set; } = "";
}

// ========== Recording ==========

public class RecordingStartRequest
{
    public string? Filename { get; set; }
}

public class RecordingReplayRequest
{
    public string Filename { get; set; } = "";
}
