namespace ACCcom.Core.Models;

public static class HexHelper
{
    private static readonly string HexChars = "0123456789ABCDEF";

    public static bool HasErrorSeverity(List<FieldAnnotation>? fields)
    {
        if (fields == null) return false;
        foreach (var f in fields)
            if (f.Severity == FieldSeverity.Error) return true;
        return false;
    }

    public static int CountHexBytes(string hex)
    {
        int count = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ') count++;
        return count / 2;
    }

    public static byte[] HexStringToBytes(string hex)
    {
        int nonSpaceLen = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ') nonSpaceLen++;
        var bytes = new byte[nonSpaceLen / 2];
        int byteIdx = 0;
        int hi = -1;
        foreach (var c in hex.AsSpan())
        {
            if (c == ' ') continue;
            int val = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => 0
            };
            if (hi < 0) hi = val;
            else { bytes[byteIdx++] = (byte)(hi << 4 | val); hi = -1; }
        }
        return bytes;
    }

    public static string BytesToHexSpaced(byte[] bytes, int offset, int count)
    {
        if (count == 0) return string.Empty;

        return string.Create(count * 3 - 1, (bytes, offset, count), static (span, state) =>
        {
            var (buf, off, len) = state;
            for (int i = 0; i < len; i++)
            {
                if (i > 0) span[i * 3 - 1] = ' ';
                span[i * 3] = HexChars[buf[off + i] >> 4];
                span[i * 3 + 1] = HexChars[buf[off + i] & 0xF];
            }
        });
    }
}
