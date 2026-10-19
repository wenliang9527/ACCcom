namespace ACCcom.Core.Services;

/// <summary>
/// CRC 校验工具类，提供统一的 CRC 算法实现
/// </summary>
public static class CrcHelper
{
    /// <summary>
    /// CRC-16/Modbus 查表（反向多项式 0xA001）。逐位实现每字节 8 次移位；
    /// RTU 主站每次请求/响应、从站每次收发都走这条路。表在类型初始化时构建一次，
    /// 结果与逐位实现逐位一致（由 CrcHelperTests 的 oracle 测试钉死）。
    /// </summary>
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int b = 0; b < 8; b++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            table[i] = crc;
        }
        return table;
    }

    /// <summary>
    /// CRC-16/Modbus 校验
    /// </summary>
    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
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
