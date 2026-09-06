namespace ACCcom.Core.Models;

/// <summary>Display row for a Modbus register read in the register table.
/// Lives in Core so the byte-decoding logic can produce it without depending
/// on the UI layer.</summary>
public class RegisterItem
{
    public ushort Address { get; set; }
    public ushort Value { get; set; }
    public string Hex => $"0x{Value:X4}";
    public ushort Dec => Value;
    public string Binary => Convert.ToString(Value, 2).PadLeft(16, '0');
}
