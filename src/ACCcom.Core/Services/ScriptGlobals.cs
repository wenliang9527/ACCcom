namespace ACCcom.Core.Services;

public class ScriptGlobals
{
    public byte[] RawData { get; set; } = [];
    public DateTime Timestamp { get; set; }

    private bool InBounds(int offset, int needed) => offset >= 0 && offset + needed <= RawData.Length;

    public string RawHex(int offset, int length)
    {
        if (!InBounds(offset, length)) return "";
        return BitConverter.ToString(RawData, offset, length).Replace("-", " ");
    }

    public ushort ToUInt16(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 2)) return 0;
        return bigEndian
            ? (ushort)((RawData[offset] << 8) | RawData[offset + 1])
            : (ushort)((RawData[offset + 1] << 8) | RawData[offset]);
    }

    public ushort Crc16(int offset, int length)
    {
        if (!InBounds(offset, 0)) return 0;
        ushort crc = 0xFFFF;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
        {
            crc ^= RawData[i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    public byte Sum8(int offset, int length)
    {
        if (!InBounds(offset, 0)) return 0;
        byte sum = 0;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
            sum += RawData[i];
        return sum;
    }

    public byte Xor8(int offset, int length)
    {
        if (!InBounds(offset, 0)) return 0;
        byte xor = 0;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
            xor ^= RawData[i];
        return xor;
    }

    public int ToInt16(int offset, bool bigEndian = false) => (short)ToUInt16(offset, bigEndian);

    public float ToFloat(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 4)) return 0;
        var bytes = RawData[offset..(offset + 4)];
        if (bigEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes);
    }
}
