using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FrameAssemblerConfigTests
{
    [Fact]
    public void ToFrameBufferConfig_maps_fields()
    {
        var config = new FrameAssemblerConfig
        {
            Header = "A5 5A",
            LengthFieldOffset = 3,
            LengthFieldSize = 2,
            MaxFrameSize = 8192,
            PartialFrameTimeoutMs = 500
        };

        var buffer = config.ToFrameBufferConfig();

        Assert.Equal(FrameExtractStrategy.ByHeader, buffer.Strategy);
        Assert.Equal(new byte[] { 0xA5, 0x5A }, buffer.Header);
        Assert.Equal(3, buffer.LengthFieldOffset);
        Assert.Equal(2, buffer.LengthFieldSize);
        // Length field includes the full frame (legacy semantics).
        Assert.Equal(0, buffer.LengthFieldIncludes);
        Assert.Equal(8192, buffer.MaxFrameSize);
        Assert.Equal(65536, buffer.BufferCapacity);
        Assert.Equal(500, buffer.PartialFrameTimeoutMs);
    }

    [Fact]
    public void ToFrameBufferConfig_defaults_passthrough()
    {
        var config = new FrameAssemblerConfig();

        var buffer = config.ToFrameBufferConfig();

        Assert.Null(buffer.Header);
        Assert.Equal(-1, buffer.LengthFieldOffset);
        Assert.Equal(1, buffer.LengthFieldSize);
        Assert.Equal(4096, buffer.MaxFrameSize);
        Assert.Equal(2000, buffer.PartialFrameTimeoutMs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToFrameBufferConfig_nonpositive_max_frame_size_clamped_to_default(int maxFrameSize)
    {
        // A non-positive max frame size would make every frame read as
        // oversized and be dropped silently — clamp to the default instead.
        var config = new FrameAssemblerConfig { MaxFrameSize = maxFrameSize };

        var buffer = config.ToFrameBufferConfig();

        Assert.Equal(4096, buffer.MaxFrameSize);
    }

    [Fact]
    public void ToFrameBufferConfig_nonpositive_timeout_clamped_to_default()
    {
        var config = new FrameAssemblerConfig { PartialFrameTimeoutMs = 0 };

        var buffer = config.ToFrameBufferConfig();

        Assert.Equal(2000, buffer.PartialFrameTimeoutMs);
    }

    [Fact]
    public void ParseHeaderBytes_space_separated()
    {
        var result = FrameAssemblerConfig.ParseHeaderBytes("A5 5A");

        Assert.Equal(new byte[] { 0xA5, 0x5A }, result);
    }

    [Fact]
    public void ParseHeaderBytes_compact_hex()
    {
        var result = FrameAssemblerConfig.ParseHeaderBytes("A55A");

        Assert.Equal(new byte[] { 0xA5, 0x5A }, result);
    }

    [Fact]
    public void ParseHeaderBytes_mixed_whitespace()
    {
        var result = FrameAssemblerConfig.ParseHeaderBytes(" A5\t5A\n");

        Assert.Equal(new byte[] { 0xA5, 0x5A }, result);
    }

    [Fact]
    public void ParseHeaderBytes_lowercase()
    {
        var result = FrameAssemblerConfig.ParseHeaderBytes("de ad");

        Assert.Equal(new byte[] { 0xDE, 0xAD }, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseHeaderBytes_empty_or_null_returns_null(string? header)
    {
        Assert.Null(FrameAssemblerConfig.ParseHeaderBytes(header));
    }

    [Theory]
    [InlineData("A5 5")]      // odd digit count
    [InlineData("GG")]        // invalid hex
    [InlineData("A5 5A HH")]  // partially invalid
    public void ParseHeaderBytes_malformed_returns_null(string header)
    {
        Assert.Null(FrameAssemblerConfig.ParseHeaderBytes(header));
    }
}