namespace ACCcom.Core.Models;

public class ModbusResponse
{
    public bool IsError { get; init; }
    public byte SlaveId { get; init; }
    public ModbusFunctionCode FunctionCode { get; init; }
    public byte[] Data { get; init; } = [];
    public byte? ExceptionCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawRequestHex { get; init; }
    public string? RawResponseHex { get; init; }
}
