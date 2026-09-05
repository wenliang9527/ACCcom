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

    // ── ByHeader strategy (the production default from FrameAssemblerConfig) ──

    private static FrameBufferConfig ByHeader(byte[] header, int capacity = 64)
    {
        return new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = header,
            LengthFieldOffset = -1, // no length field: header-to-end is the frame
            BufferCapacity = capacity,
            MaxFrameSize = 64
        };
    }

    [Fact]
    public void ByHeader_AssemblesFromHeaderToEnd()
    {
        var buffer = Create(ByHeader([0xA5, 0x5A]));
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0xA5, 0x5A, 0x01, 0x02, 0x03]);

        var frame = Assert.Single(frames);
        Assert.Equal([0xA5, 0x5A, 0x01, 0x02, 0x03], frame);
    }

    [Fact]
    public void ByHeader_SkipsLeadingGarbageBeforeHeader()
    {
        var buffer = Create(ByHeader([0xA5, 0x5A]));
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        // Leading non-header bytes are discarded; frame starts at the header.
        buffer.Write([0x11, 0x22, 0xA5, 0x5A, 0xAA, 0xBB]);

        var frame = Assert.Single(frames);
        Assert.Equal([0xA5, 0x5A, 0xAA, 0xBB], frame);
    }

    [Fact]
    public void ByHeader_HeaderSplitAcrossWrites_AssemblesOnce()
    {
        // The 2-byte header itself arrives split across writes; the frame is
        // emitted only once the header is complete, then buffered data after it
        // is one frame (no length field).
        var buffer = Create(ByHeader([0xA5, 0x5A]));
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0xA5]);               // half the header
        Assert.Empty(frames);

        buffer.Write([0x5A, 0x01, 0x02]);   // completes header + payload
        var frame = Assert.Single(frames);
        Assert.Equal([0xA5, 0x5A, 0x01, 0x02], frame);
    }

    [Fact]
    public void ByHeader_OversizeFrame_ResetsWithoutEmitting()
    {
        // Length field at offset 2 (after the 2-byte header); its value (0xFF)
        // yields a frame length far above MaxFrameSize, so the buffer resets and
        // emits nothing rather than emitting a corrupted giant frame.
        var config = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = [0xA5, 0x5A],
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            LengthFieldIncludes = 0,
            BufferCapacity = 16,
            MaxFrameSize = 64
        };
        var buffer = Create(config);
        var frames = new List<byte[]>();
        buffer.OnFrameAssembled += e => frames.Add(HexHelper.HexStringToBytes(e.RawHex));

        buffer.Write([0xA5, 0x5A, 0xFF, 0x01, 0x02]);

        Assert.Empty(frames);
    }

    [Fact]
    public void EmitFrame_AssignsMonotonicIds()
    {
        // Regression: the merged RX path previously emitted entries with Id == 0,
        // which broke DataBufferService.GetEntriesSince (HTTP dashboard polling).
        var buffer = Create(FixedLengthConfig());
        var ids = new List<long>();
        buffer.OnFrameAssembled += e => ids.Add(e.Id);

        buffer.Write([0x01, 0x02, 0x03]);
        buffer.Write([0x04, 0x05, 0x06]);

        // Ids come from a process-wide static counter, so they are monotonic but
        // not necessarily starting at 1; the invariant is strictly increasing.
        Assert.Equal(2, ids.Count);
        Assert.True(ids[1] > ids[0]);
        Assert.True(ids[0] > 0);
    }

    private static FrameBufferConfig FixedLengthConfig()
    {
        return new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.FixedLength,
            FixedLength = 3,
            BufferCapacity = 16,
            MaxFrameSize = 16
        };
    }
}