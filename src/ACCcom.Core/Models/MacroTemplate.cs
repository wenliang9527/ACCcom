namespace ACCcom.Core.Models;

public class MacroTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = new();
    public int RepeatCount { get; set; } = 1; // 0 = infinite
    public int RepeatDelayMs { get; set; } = 0;
}

public class MacroStep
{
    public string Command { get; set; } = "";
    public bool IsHex { get; set; }
    public int DelayMs { get; set; } = 0;       // delay after this step
    public string? WaitFor { get; set; }         // wait for response pattern before continuing
    public int WaitTimeoutMs { get; set; } = 3000;
    public string? Condition { get; set; }       // e.g. "contains:OK" - skip step if condition not met on previous response
}
