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
        var bytes = (byte[])RawData[offset..(offset + 4)].Clone();
        if (bigEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes);
    }

    public uint ToUInt32(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 4)) return 0;
        return bigEndian
            ? (uint)((RawData[offset] << 24) | (RawData[offset + 1] << 16) | (RawData[offset + 2] << 8) | RawData[offset + 3])
            : (uint)(RawData[offset] | (RawData[offset + 1] << 8) | (RawData[offset + 2] << 16) | (RawData[offset + 3] << 24));
    }

    public int ToInt32(int offset, bool bigEndian = false) => (int)ToUInt32(offset, bigEndian);

    public double ToDouble(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 8)) return 0;
        var bytes = (byte[])RawData[offset..(offset + 8)].Clone();
        if (bigEndian) Array.Reverse(bytes);
        return BitConverter.ToDouble(bytes);
    }

    public int FromBcd(int offset, int length)
    {
        if (!InBounds(offset, length)) return 0;
        int result = 0;
        for (int i = 0; i < length; i++)
        {
            var b = RawData[offset + i];
            result = result * 100 + (b >> 4) * 10 + (b & 0x0F);
        }
        return result;
    }

}
