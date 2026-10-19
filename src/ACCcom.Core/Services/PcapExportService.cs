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

    public void ExportToPcap(IEnumerable<LogEntry>? entries, string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // Defensive: a null collection or a null entry would otherwise NRE in
        // WritePacketRecord. Skip both and keep writing the rest.
        if (entries == null) return;

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        WriteGlobalHeader(writer);

        foreach (var entry in entries)
        {
            // One malformed entry (e.g. from a hand-edited replay file) must not
            // abort the whole export — skip it and keep writing the rest.
            try
            {
                if (entry == null) continue;
                WritePacketRecord(writer, entry);
            }
            catch (FormatException)
            {
                // Skip: entry's RawHex is not parsable hex.
            }
            catch (ArgumentException)
            {
                // Skip: same, defensive against odd-length/edge strings.
            }
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

    /// <summary>Strict hex parse via the shared HexHelper contract. The previous
    /// private parser silently left-padded odd digit runs with "0" and padded
    /// nothing for illegal characters until Convert.ToByte threw mid-loop —
    /// exporting a WRONG packet is harder to diagnose than a skipped one, and
    /// the export loop already skips a malformed entry (FormatException).</summary>
    internal static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return [];

        if (!HexHelper.TryHexStringToBytes(hex, out var bytes))
            throw new FormatException($"Invalid hex payload for pcap export: '{hex}'");
        return bytes;
    }
}
