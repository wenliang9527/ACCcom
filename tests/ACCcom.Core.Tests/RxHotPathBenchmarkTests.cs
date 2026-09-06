using System.Diagnostics;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Coarse hot-path throughput checks for the RX pipeline. Assertions use very
/// wide lower bounds (10x+ below what the optimized path sustains) so CI on
/// slow machines or under load never flakes, while a real regression — e.g.
/// reintroducing a lock or per-frame allocation — that cuts throughput by an
/// order of magnitude still trips the bound.
/// </summary>
[Collection("SerialTcp")]
public class RxHotPathBenchmarkTests
{
    private static FrameBuffer CreateLengthFieldBuffer()
    {
        var config = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByLengthField,
            LengthFieldOffset = 0,
            LengthFieldSize = 1,
            LengthFieldIncludes = 0,
            BufferCapacity = 65536,
            MaxFrameSize = 4096
        };
        return new FrameBuffer(config);
    }

    [Fact]
    public void FrameBuffer_Parsing_SustainsHighThroughput()
    {
        using var buffer = CreateLengthFieldBuffer();
        // One 8-byte frame per write: [len=8][7 payload bytes].
        var frame = new byte[8];
        frame[0] = 8;
        for (int i = 1; i < 8; i++) frame[i] = (byte)i;

        int emitted = 0;
        buffer.OnFrameAssembled += _ => emitted++;

        const int count = 200_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
            buffer.Write(frame);
        sw.Stop();

        Assert.Equal(count, emitted);
        // Wide bound: optimized path does ~millions/sec; anything under 100k
        // frames/sec indicates a serious regression.
        double fps = count / sw.Elapsed.TotalSeconds;
        Assert.True(fps > 100_000, $"FrameBuffer throughput too low: {fps:F0} fps");
    }

    [Fact]
    public void DataBufferService_Append_SustainsHighThroughput()
    {
        using var buffer = new DataBufferService(10_000);

        const int count = 500_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            buffer.AddEntry(new LogEntry
            {
                Id = i + 1,
                Direction = "RX",
                RawHex = "AA 55 01",
                Text = "x"
            });
        }
        sw.Stop();

        // Ring capacity 10_000: after 500k appends only the newest 10k remain.
        Assert.Equal(10_000, buffer.CountWhere(_ => true));
        double appendPerSec = count / sw.Elapsed.TotalSeconds;
        Assert.True(appendPerSec > 500_000, $"Append throughput too low: {appendPerSec:F0}/s");
    }

    [Fact]
    public void GetEntriesSince_TailPoll_SustainsHighThroughput()
    {
        using var buffer = new DataBufferService(10_000);
        for (int i = 0; i < 10_000; i++)
        {
            buffer.AddEntry(new LogEntry { Id = i + 1, Direction = "RX", Text = "x" });
        }

        const int polls = 100_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < polls; i++)
            buffer.GetEntriesSince(9_900);
        sw.Stop();

        double pollsPerSec = polls / sw.Elapsed.TotalSeconds;
        // Binary-search tail poll: optimized path does ~1M+/sec.
        Assert.True(pollsPerSec > 100_000, $"Poll throughput too low: {pollsPerSec:F0}/s");
    }

    [Fact]
    public void HexHelper_ParseAndFormat_SustainsHighThroughput()
    {
        const string hex = "AA 55 03 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F";
        var bytes = HexHelper.HexStringToBytes(hex);

        const int count = 200_000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            var parsed = HexHelper.HexStringToBytes(hex);
            HexHelper.BytesToHexSpaced(parsed, 0, parsed.Length);
        }
        sw.Stop();

        double opsPerSec = count / sw.Elapsed.TotalSeconds;
        Assert.True(opsPerSec > 100_000, $"Hex ops too slow: {opsPerSec:F0}/s ({bytes.Length} bytes)");
    }
}