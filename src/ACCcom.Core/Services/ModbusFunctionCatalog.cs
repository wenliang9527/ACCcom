using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// The authoritative catalog of Modbus function codes shown in the UI dropdown:
/// display name, protocol code and dropdown index are defined once here and
/// kept in lockstep. Previously the index→enum switch (GetFunctionByIndex) and
/// the display-name list (FunctionNames) lived in ModbusViewModel as two
/// parallel arrays that could silently drift.
/// </summary>
public static class ModbusFunctionCatalog
{
    /// <summary>Dropdown entries in display order. Index in this list is the
    /// dropdown index; Name is the display string; Code is the protocol code.</summary>
    private static readonly (ModbusFunctionCode Code, string Name)[] Entries =
    [
        (ModbusFunctionCode.ReadCoils, "01 Read Coils"),
        (ModbusFunctionCode.ReadDiscreteInputs, "02 Read Discrete Inputs"),
        (ModbusFunctionCode.ReadHoldingRegisters, "03 Read Holding Registers"),
        (ModbusFunctionCode.ReadInputRegisters, "04 Read Input Registers"),
        (ModbusFunctionCode.WriteSingleCoil, "05 Write Single Coil"),
        (ModbusFunctionCode.WriteSingleRegister, "06 Write Single Register"),
        (ModbusFunctionCode.WriteMultipleCoils, "15 Write Multiple Coils"),
        (ModbusFunctionCode.WriteMultipleRegisters, "16 Write Multiple Registers"),
        (ModbusFunctionCode.MaskWriteRegister, "22 Mask Write Register"),
        (ModbusFunctionCode.ReadWriteMultipleRegisters, "23 Read/Write Multiple Registers")
    ];

    /// <summary>Display names in dropdown order.</summary>
    public static IReadOnlyList<string> DisplayNames
        => Entries.Select(e => e.Name).ToArray();

    /// <summary>Maps a dropdown index to its function code. Out-of-range
    /// indices fall back to ReadHoldingRegisters, matching the original
    /// switch's default.</summary>
    public static ModbusFunctionCode FromIndex(int index)
        => index >= 0 && index < Entries.Length ? Entries[index].Code : ModbusFunctionCode.ReadHoldingRegisters;

    /// <summary>Maps a function code to its dropdown index, or -1 when the
    /// code is not in the catalog.</summary>
    public static int ToIndex(ModbusFunctionCode code)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (Entries[i].Code == code) return i;
        return -1;
    }

    /// <summary>Returns the display name for a function code, or the raw code
    /// string when not in the catalog.</summary>
    public static string GetDisplayName(ModbusFunctionCode code)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (Entries[i].Code == code) return Entries[i].Name;
        return ((byte)code).ToString();
    }
}
