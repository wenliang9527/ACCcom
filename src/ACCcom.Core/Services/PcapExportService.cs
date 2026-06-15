using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class PcapExportService
{
    private const uint MagicNumber = 0xA1B2C3D4;
    private const ushort PcapVersionMajor = 2;
    private const ushort PcapVersionMinor = 4;
    private const uint SnapLen = 65535;
    private const uint LinkTypeUser = 249;

    private const byte DirectionTx = 0x01;
    private const byte DirectionRx = 0x02;

    public void ExportToPcap(IEnumerable<LogEntry> entries, string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        WriteGlobalHeader(writer);

        foreach (var entry in entries)
        {
            WritePacketRecord(writer, entry);
        }
    }

    private static void WriteGlobalHeader(BinaryWriter writer)
    {
        writer.Write(MagicNumber);
        writer.Write(PcapVersionMajor);
        writer.Write(PcapVersionMinor);
        writer.Write(0); // Timezone
        writer.Write(0); // Sigfigs
        writer.Write(SnapLen);
        writer.Write(LinkTypeUser);
    }

    private static void WritePacketRecord(BinaryWriter writer, LogEntry entry)
    {
        var timestamp = entry.Timestamp;
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var seconds = (uint)(timestamp - epoch).TotalSeconds;
        var microseconds = (uint)(timestamp.Millisecond * 1000);

        var directionByte = entry.Direction == "TX" ? DirectionTx : DirectionRx;
        var dataBytes = HexToBytes(entry.RawHex);

        var packetData = new byte[dataBytes.Length + 1];
        packetData[0] = directionByte;
        Array.Copy(dataBytes, 0, packetData, 1, dataBytes.Length);

        writer.Write(seconds);
        writer.Write(microseconds);
        writer.Write((uint)packetData.Length);
        writer.Write((uint)packetData.Length);
        writer.Write(packetData);
    }

    internal static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return [];

        var cleaned = hex.Replace(" ", "").Replace("-", "");
        if (cleaned.Length % 2 != 0)
            cleaned = "0" + cleaned;

        var bytes = new byte[cleaned.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
