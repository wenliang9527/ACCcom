namespace ACCcom.Core.Services;

public class ScriptGlobals
{
    public byte[] RawData { get; set; } = [];
    public DateTime Timestamp { get; set; }

    private bool InBounds(int offset, int needed) => offset >= 0 && offset + needed <= RawData.Length;

    public string RawHex(int offset, int length)
    {
        if (!InBounds(offset, length)) return "";
        return string.Create(length * 3 - 1, (RawData, offset, length), static (span, state) =>
        {
            var (data, off, len) = state;
            for (int i = 0; i < len; i++)
            {
                if (i > 0) span[i * 3 - 1] = ' ';
                span[i * 3] = HexChar(data[off + i] >> 4);
                span[i * 3 + 1] = HexChar(data[off + i] & 0xF);
            }
        });
    }

    private static char HexChar(int val) => (char)(val < 10 ? '0' + val : 'A' + val - 10);

    public ushort ToUInt16(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 2)) return 0;
        return bigEndian
            ? (ushort)((RawData[offset] << 8) | RawData[offset + 1])
            : (ushort)((RawData[offset + 1] << 8) | RawData[offset]);
    }

    public ushort Crc16(int offset, int length)
    {
        if (!InBounds(offset, length)) return 0;
        return CrcHelper.Crc16(RawData, offset, length);
    }

    public byte Sum8(int offset, int length)
    {
        if (!InBounds(offset, length)) return 0;
        return CrcHelper.Sum8(RawData.AsSpan(offset, length));
    }

    public byte Xor8(int offset, int length)
    {
        if (!InBounds(offset, length)) return 0;
        return CrcHelper.Xor8(RawData.AsSpan(offset, length));
    }

    public ushort Sum16(int offset, int length)
    {
        if (!InBounds(offset, length)) return 0;
        return CrcHelper.Sum16(RawData.AsSpan(offset, length));
    }

    public int ToInt16(int offset, bool bigEndian = false) => (short)ToUInt16(offset, bigEndian);

    public float ToFloat(int offset, bool bigEndian = false)
    {
        if (!InBounds(offset, 4)) return 0;
        Span<byte> bytes = stackalloc byte[4];
        RawData.AsSpan(offset, 4).CopyTo(bytes);
        if (bigEndian) bytes.Reverse();
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
        Span<byte> bytes = stackalloc byte[8];
        RawData.AsSpan(offset, 8).CopyTo(bytes);
        if (bigEndian) bytes.Reverse();
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
