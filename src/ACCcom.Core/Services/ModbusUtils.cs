namespace ACCcom.Core.Services;

public static class ModbusUtils
{
    public static List<(ushort start, ushort count)> MergeRanges(ushort startAddr, ushort totalCount, ushort maxPerRequest = 125)
    {
        var ranges = new List<(ushort, ushort)>();
        ushort remaining = totalCount;
        ushort current = startAddr;
        while (remaining > 0)
        {
            var chunk = remaining > maxPerRequest ? maxPerRequest : remaining;
            ranges.Add((current, chunk));
            current += chunk;
            remaining -= chunk;
        }
        return ranges;
    }
}
