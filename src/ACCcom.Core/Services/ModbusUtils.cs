namespace ACCcom.Core.Services;

public static class ModbusUtils
{
    public static List<(ushort start, ushort count)> MergeRanges(ushort startAddr, ushort totalCount, ushort maxPerRequest = 125)
    {
        var ranges = new List<(ushort, ushort)>();
        if (totalCount == 0) return ranges;

        // Clamp to a positive chunk size: a 0 maxPerRequest would make the
        // loop below never advance (chunk stays 0) and spin forever.
        var chunkSize = maxPerRequest == 0 ? (ushort)1 : maxPerRequest;

        ushort remaining = totalCount;
        ushort current = startAddr;
        while (remaining > 0)
        {
            var chunk = remaining > chunkSize ? chunkSize : remaining;
            ranges.Add((current, chunk));
            current += chunk;
            remaining -= chunk;
        }
        return ranges;
    }
}
