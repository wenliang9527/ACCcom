using ACCcom.Core.Models;

namespace ACCcom.Core.Models;

/// <summary>Display row for a Modbus transaction in the log window. Lives in
/// Core so the log exporters (CSV/JSON/TXT) can format it without depending
/// on the UI layer.</summary>
public class TransactionLogItem
{
    public DateTime Timestamp { get; set; }
    public ModbusFunctionCode FunctionCode { get; set; }
    public byte SlaveId { get; set; }
    public string RequestHex { get; set; } = "";
    public string ResponseHex { get; set; } = "";
    public string Status { get; set; } = "";
}
