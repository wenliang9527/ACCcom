using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Decodes a Modbus read-response data payload into register rows. Each
/// register is a big-endian uint16 pair; addresses increment from a base.
/// An odd trailing byte (data.Length not even) is dropped — the protocol
/// guarantees even-length payloads for register reads, so a stray byte is
/// framing garbage, not a register. Extracted from ModbusViewModel so the
/// byte-splitting semantics are unit-testable.
/// </summary>
public static class ModbusRegisterDecoder
{
    /// <summary>Decodes <paramref name="data"/> into register items starting at
    /// <paramref name="baseAddr"/>. Returns an empty list when there are fewer
    /// than 2 bytes.</summary>
    public static List<RegisterItem> Decode(byte[] data, ushort baseAddr)
    {
        var registers = new List<RegisterItem>();
        if (data.Length < 2) return registers;

        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            var addr = (ushort)(baseAddr + (i / 2));
            var value = (ushort)((data[i] << 8) | data[i + 1]);
            registers.Add(new RegisterItem { Address = addr, Value = value });
        }
        return registers;
    }
}
