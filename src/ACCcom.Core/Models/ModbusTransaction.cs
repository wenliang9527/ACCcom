namespace ACCcom.Core.Models;

public class ModbusTransaction
{
    public DateTime Timestamp { get; init; }
    public ModbusFunctionCode FunctionCode { get; init; }
    public byte SlaveId { get; init; }
    public string RequestHex { get; init; } = "";
    public string? ResponseHex { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public ModbusResponse? Response { get; init; }
}
