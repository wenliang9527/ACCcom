namespace ACCcom.Core.Models;

public enum FieldSeverity
{
    Normal,
    Warning,
    Error
}

public class FieldAnnotation
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
    public int Length { get; set; }
    public string RawHex { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string? Color { get; set; }
    public FieldSeverity Severity { get; set; }
}
