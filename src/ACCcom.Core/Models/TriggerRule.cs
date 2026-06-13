namespace ACCcom.Core.Models;

public class TriggerRule
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public string MatchMode { get; set; } = "contains";
    public bool MatchHex { get; set; }
    public string? Direction { get; set; }
    public TriggerAction Action { get; set; } = TriggerAction.None;
    public string? ActionParameter { get; set; }
    public bool Enabled { get; set; } = true;
}

public enum TriggerAction
{
    None,
    SendCommand,
    SaveToFile,
    PlaySound,
    LogMessage
}
