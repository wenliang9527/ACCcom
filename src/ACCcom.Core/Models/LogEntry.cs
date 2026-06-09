namespace ACCcom.Core.Models;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = ""; // "RX" or "TX"
    public string RawHex { get; set; } = "";
    public string Text { get; set; } = "";
    public List<FieldAnnotation>? Fields { get; set; }
}
