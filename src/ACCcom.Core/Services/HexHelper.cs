namespace ACCcom.Core.Services;

internal static class HexHelper
{
    private static readonly string HexChars = "0123456789ABCDEF";

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
