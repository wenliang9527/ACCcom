using System.Globalization;

namespace ACCcom.Core.Services;

/// <summary>
/// Parses the user's comma-separated batch-value input for Modbus write
/// operations. Coil values accept "1"/"true"/"on"/"yes" (anything else is
/// false); register values accept decimal or 0x-prefixed hex, with unparsable
/// tokens silently becoming 0. Extracted from ModbusViewModel so the token
/// semantics are unit-testable without the UI layer.
/// </summary>
public static class ModbusValueParser
{
    /// <summary>Parses coil flags from a comma-separated string
    /// ("1,0,true,on,no" → true,false,true,true,false). Empty/whitespace input
    /// yields an empty array.</summary>
    public static bool[] ParseCoilValues(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s is "1" or "true" or "on" or "yes").ToArray();
    }

    /// <summary>Parses register values from a comma-separated string. Tokens
    /// prefixed with 0x (case-insensitive) are parsed as hex; everything else
    /// is parsed as a decimal ushort. Unparsable tokens become 0 rather than
    /// throwing, so a stray space or typo can't crash a batch write.
    /// Empty/whitespace input yields an empty array.</summary>
    public static ushort[] ParseRegisterValues(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ushort.Parse(s[2..], NumberStyles.HexNumber)
                : ushort.TryParse(s, out var v) ? v : (ushort)0)
            .ToArray();
    }
}