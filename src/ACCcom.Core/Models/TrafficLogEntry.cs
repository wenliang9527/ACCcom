namespace ACCcom.Core.Models;

/// <summary>One parsed line of the MCP traffic JSONL log, shaped for display:
/// the timestamp is sliced to HH:mm:ss.fff and missing fields default to empty
/// strings instead of null. Mutable because the parser fills the entry field by
/// field after construction.</summary>
public sealed class TrafficLogEntry
{
    public string Time { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Tool { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Text { get; set; } = "";
    public string Hex { get; set; } = "";
}
