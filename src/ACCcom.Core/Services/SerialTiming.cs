using System;

namespace ACCcom.Core.Services;

/// <summary>
/// Serial-line timing math shared by auto-send features. Kept free of WPF so
/// the minimum-interval contract is unit-testable.
/// </summary>
public static class SerialTiming
{
    /// <summary>Start(1) + data(8) + stop(1) for the default 8N1 frame.</summary>
    public const int BitsPerByte = 10;

    /// <summary>Used when no baud rate is available (port closed / invalid input).</summary>
    public const int DefaultBaudRate = 115200;

    /// <summary>
    /// Minimum interval (ms) between transmissions of a payload so the UART is
    /// free before the next frame: ceil(bytes * 10 * 1000 / baud) + 1 ms safety
    /// margin for OS timer granularity.
    /// </summary>
    public static int MinIntervalMs(int byteLength, int baudRate)
    {
        if (baudRate <= 0) baudRate = DefaultBaudRate;
        if (byteLength < 1) byteLength = 1;
        double wireMs = byteLength * (double)BitsPerByte * 1000.0 / baudRate;
        return Math.Max(1, (int)Math.Ceiling(wireMs) + 1);
    }
}
