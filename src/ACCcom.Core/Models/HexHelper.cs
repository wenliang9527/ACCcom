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

    /// <summary>
    /// Result of validating a user-typed hex payload in the send box. <see cref="IsValid"/>
    /// is true when the string is empty, contains only hex digits and spaces, and has an
    /// even number of hex digits. <see cref="InvalidIndex"/> points at the first offending
    /// character (for caret-style UI feedback), or -1 when valid.
    /// </summary>
    public readonly struct HexValidationResult
    {
        public bool IsValid { get; }
        public int InvalidIndex { get; }
        public int ByteCount { get; }

        public HexValidationResult(bool isValid, int invalidIndex, int byteCount)
        {
            IsValid = isValid;
            InvalidIndex = invalidIndex;
            ByteCount = byteCount;
        }
    }

    /// <summary>
    /// Validates a hex payload typed by the user. Empty strings are valid (a no-op send).
    /// Whitespace is permitted between byte pairs; embedded newlines and tabs are accepted
    /// because the send box is multi-line tolerant.
    /// </summary>
    public static HexValidationResult ValidateHexInput(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return new HexValidationResult(true, -1, 0);
        int digitCount = 0;
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
            if (!IsHexDigit(c)) return new HexValidationResult(false, i, digitCount / 2);
            digitCount++;
        }
        if ((digitCount & 1) != 0) return new HexValidationResult(false, hex.Length, digitCount / 2);
        return new HexValidationResult(true, -1, digitCount / 2);
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

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
