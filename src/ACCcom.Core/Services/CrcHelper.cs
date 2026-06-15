namespace ACCcom.Core.Services;

/// <summary>
/// CRC 校验工具类，提供统一的 CRC 算法实现
/// </summary>
public static class CrcHelper
{
    /// <summary>
    /// CRC-16/Modbus 校验
    /// </summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    /// <summary>
    /// CRC-16/Modbus 校验（从字节数组指定偏移开始）
    /// </summary>
    public static ushort Crc16(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
            return 0;
        return Crc16(data.AsSpan(offset, length));
    }

    /// <summary>
    /// Sum8 校验
    /// </summary>
    public static byte Sum8(ReadOnlySpan<byte> data)
    {
        byte sum = 0;
        foreach (var b in data)
            sum += b;
        return sum;
    }

    /// <summary>
    /// Xor8 校验
    /// </summary>
    public static byte Xor8(ReadOnlySpan<byte> data)
    {
        byte xor = 0;
        foreach (var b in data)
            xor ^= b;
        return xor;
    }

    /// <summary>
    /// Sum16 校验
    /// </summary>
    public static ushort Sum16(ReadOnlySpan<byte> data)
    {
        ushort sum = 0;
        foreach (var b in data)
            sum += b;
        return sum;
    }
}
