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
        HexStringToBytes(hex, bytes);
        return bytes;
    }

    /// <summary>
    /// Lenient variant of <see cref="HexStringToBytes(string)"/> that parses into a
    /// caller-provided buffer (e.g. an ArrayPool-rented array) instead of allocating.
    /// Returns the number of bytes written; the trailing high nibble of an odd digit
    /// count is dropped, matching the allocating variant. Input larger than
    /// <paramref name="destination"/> is truncated to the buffer capacity.
    /// </summary>
    public static int HexStringToBytes(string hex, Span<byte> destination)
    {
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
            else
            {
                if (byteIdx < destination.Length)
                    destination[byteIdx] = (byte)(hi << 4 | val);
                byteIdx++;
                hi = -1;
            }
        }
        return Math.Min(byteIdx, destination.Length);
    }

    /// <summary>
    /// Strict variant of <see cref="HexStringToBytes"/> that surfaces invalid input
    /// instead of silently substituting zero nibbles. Returns false (and outputs an
    /// empty array) when the input contains characters other than hex digits /
    /// whitespace, or when the number of hex digits is odd.
    ///
    /// New call sites should prefer this over the legacy lenient parser, which can
    /// mask real data corruption by turning an invalid nibble into 0 (e.g. a real
    /// 0xZ1 byte would be read as 0x01 with no warning).
    /// </summary>
    public static bool TryHexStringToBytes(string? hex, out byte[] bytes)
    {
        if (string.IsNullOrEmpty(hex)) { bytes = Array.Empty<byte>(); return true; }
        int digitCount = 0;
        foreach (var c in hex.AsSpan())
            if (c != ' ' && c != '\t' && c != '\r' && c != '\n') digitCount++;
        if ((digitCount & 1) != 0) { bytes = Array.Empty<byte>(); return false; }

        var buf = new byte[digitCount / 2];
        int byteIdx = 0;
        int hi = -1;
        foreach (var c in hex.AsSpan())
        {
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
            int val;
            if (c >= '0' && c <= '9') val = c - '0';
            else if (c >= 'A' && c <= 'F') val = c - 'A' + 10;
            else if (c >= 'a' && c <= 'f') val = c - 'a' + 10;
            else { bytes = Array.Empty<byte>(); return false; }

            if (hi < 0) hi = val;
            else { buf[byteIdx++] = (byte)((hi << 4) | val); hi = -1; }
        }
        bytes = buf;
        return true;
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
