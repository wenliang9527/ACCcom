namespace ACCcom.Core.Services;

/// <summary>User-facing frame-assembly settings (bound in the UI). Mapping to
/// the internal <see cref="FrameBufferConfig"/> lives here so the header-string
/// parsing and length-field defaults are unit-testable without a UI layer.</summary>
public class FrameAssemblerConfig
{
    public string Header { get; set; } = "";
    public int LengthFieldOffset { get; set; } = -1;
    public int LengthFieldSize { get; set; } = 1;
    public int MaxFrameSize { get; set; } = 4096;
    public int PartialFrameTimeoutMs { get; set; } = 2000;
    public bool Enabled { get; set; }

    /// <summary>Maps this user config to a FrameBufferConfig. The strategy is
    /// always ByHeader and the length field covers the full frame (Includes=0),
    /// matching the legacy assembler semantics.</summary>
    public FrameBufferConfig ToFrameBufferConfig()
        => new()
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = ParseHeaderBytes(Header),
            LengthFieldOffset = LengthFieldOffset,
            LengthFieldSize = LengthFieldSize,
            LengthFieldIncludes = 0,
            // A non-positive max frame size would make every frame read as
            // oversized and be dropped silently (frameLen > MaxFrameSize is
            // always true); a non-positive timeout would fire immediately.
            // Clamp to the defaults instead of propagating a broken config.
            MaxFrameSize = MaxFrameSize > 0 ? MaxFrameSize : 4096,
            BufferCapacity = 65536,
            PartialFrameTimeoutMs = PartialFrameTimeoutMs > 0 ? PartialFrameTimeoutMs : 2000
        };

    /// <summary>Parses a space-separated hex header string (e.g. "A5 5A") into
    /// bytes; null when empty or malformed (FrameBuffer treats null header as
    /// "no header — whole buffer is a frame", matching the legacy assembler's
    /// empty-header behavior of assembling everything).</summary>
    public static byte[]? ParseHeaderBytes(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        try
        {
            var stripped = new string(header.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (stripped.Length == 0 || stripped.Length % 2 != 0) return null;
            return Convert.FromHexString(stripped);
        }
        catch { return null; }
    }
}
