using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class FrameBufferTests
{
    private static FrameBuffer Create(FrameBufferConfig config) => new(config);

    private static FrameBufferConfig LengthField(int capacity = 64, int includes = 0)
    {
        return new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByLengthField,
            LengthFieldOffset = 0,
            LengthFieldSize = 1,
            LengthFieldIncludes = includes,
            BufferCapacity = capacity,
            MaxFrameSize = 64
        };
    }

    [Fact]
    public void ByLengthField_ExtractsCompleteFrame()
    {
        // Length field value = total frame size including the length byte itself
        // (LengthFieldIncludes = 0). 0x03 -> 3-byte frame [03 AA BB].
        var buffer = Create(LengthField());
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0x03, 0xAA, 0xBB, 0xCC]);

        var frame = Assert.Single(frames);
        Assert.Equal([0x03, 0xAA, 0xBB], frame);
    }

    [Fact]
    public void ByLengthField_PartialThenComplete_AssemblesOnce()
    {
        var buffer = Create(LengthField());
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0x03, 0xAA]);          // 2 of 3 bytes
        Assert.Empty(frames);

        buffer.Write([0xBB]);                // completes the 3-byte frame
        var frame = Assert.Single(frames);
        Assert.Equal([0x03, 0xAA, 0xBB], frame);
    }

    [Fact]
    public void FixedLength_TailWraps_CopiesAcrossRingCorrectly()
    {
        // Capacity 5, fixed 3-byte frames. The write pattern forces the tail past
        // the end of the ring, so block copies must handle the wrap.
        var config = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.FixedLength,
            FixedLength = 3,
            BufferCapacity = 5,
            MaxFrameSize = 16
        };
        var buffer = Create(config);
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0x01, 0x02]);          // partial
        buffer.Write([0x03, 0x04, 0x05]);    // frame [01 02 03], leftover [04 05]
        buffer.Write([0x06, 0x07, 0x08]);    // tail wraps; frame [04 05 06], leftover [07 08]

        Assert.Equal(
            [new byte[] { 0x01, 0x02, 0x03 }, new byte[] { 0x04, 0x05, 0x06 }],
            frames);
    }

    [Fact]
    public void FixedLength_InputLargerThanRing_KeepsNewestBytes()
    {
        var config = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.FixedLength,
            FixedLength = 4,
            BufferCapacity = 4,
            MaxFrameSize = 16
        };
        var buffer = Create(config);
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        // 6 bytes into a 4-byte ring: the oldest two (01 02) are dropped.
        buffer.Write([0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);

        var frame = Assert.Single(frames);
        Assert.Equal([0x03, 0x04, 0x05, 0x06], frame);
    }
}