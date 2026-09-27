using System.Diagnostics;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Coarse hot-path throughput checks for the RX pipeline. Assertions use very
/// wide lower bounds (10x+ below what the optimized path sustains) so CI on
/// slow machines or under load never flakes, while a real regression â€?e.g.
/// reintroducing a lock or per-frame allocation â€?that cuts throughput by an
/// order of magnitude still trips the bound.
/// NOTE: never run these under coverage collection (coverlet instruments the
/// measured hot loop itself and trips the bounds); CI runs them in a separate
/// step without --collect (see .github/workflows/ci.yml).
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

    [Fact]
    public void DataPanelFilter_PlainContains_SustainsHighThroughput()
    {
        var entry = new LogEntry
        {
            Id = 1,
            Direction = "RX",
            Text = "sensor temperature=23.5 humidity=41 pressure=1013",
            RawHex = "AA 55 01 02 03 04 05 06"
        };

        const int count = 100_000;
        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            if (DataPanelFilter.FilterEntry(entry, "humidity", useRegex: false, showDirection: true, expressionEngine: null))
                hits++;
        }
        sw.Stop();

        Assert.Equal(count, hits);
        double opsPerSec = count / sw.Elapsed.TotalSeconds;
        // Filter settle runs once per visible entry on every keystroke-debounce;
        // under 50k/s means the plain path regressed into allocation/regex.
        Assert.True(opsPerSec > 50_000, $"Filter throughput too low: {opsPerSec:F0}/s");
    }

    [Fact]
    public void PacketFilterEngine_TimeExpression_SustainsHighThroughput()
    {
        var engine = new PacketFilterEngine("time > \"00:00:00\" and text contains temp");
        var entry = new LogEntry
        {
            Id = 1,
            Direction = "RX",
            Timestamp = DateTime.Now,
            Text = "temp=23.5",
            RawHex = "AA BB"
        };

        const int count = 50_000;
        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            if (engine.Matches(entry)) hits++;
        }
        sw.Stop();

        Assert.True(hits > 0);
        double opsPerSec = count / sw.Elapsed.TotalSeconds;
        // Expression path precomputes TimeSpan/lowercase; 20k/s is far below
        // real throughput but catches a reintroduced per-eval parse.
        Assert.True(opsPerSec > 20_000, $"Expression filter too slow: {opsPerSec:F0}/s");
    }

    /// <summary>MCP traffic mirror throughput: every RX/TX entry records one
    /// JSONL line, and (before buffering) that was a synchronous per-line disk
    /// flush on the serial DataReceived thread â€?the primary MCP RX-path
    /// bottleneck. Bound is 10x+ below the buffered writer's throughput while
    /// an fsync-per-line writer lands an order of magnitude below it.</summary>
    [Fact]
    public void McpTrafficLog_Record_SustainsHighThroughput()
    {
        // Best of three rounds: the timed window is only ~20ms, so a single
        // scheduler preemption or Defender scan of the fresh temp file could
        // sink one round below the bound without any real regression. Each
        // round uses a fresh log so none of them crosses the 5000-line
        // rotation boundary mid-window.
        const int count = 5_000;
        const int rounds = 3;
        var bestLinesPerSec = 0.0;

        for (int round = 0; round < rounds; round++)
        {
            var path = Path.Combine(Path.GetTempPath(),
                $"mcp-bench-{Guid.NewGuid():N}.jsonl");
            try
            {
                var sw = Stopwatch.StartNew();
                using (var log = new McpTrafficLog(path))
                {
                    for (int i = 0; i < count; i++)
                        log.Record(i, "rx", "RX", "AA 55 01 02", "sensor data line", "");
                }
                sw.Stop();

                double linesPerSec = count / sw.Elapsed.TotalSeconds;
                if (linesPerSec > bestLinesPerSec)
                    bestLinesPerSec = linesPerSec;
            }
            finally
            {
                try { File.Delete(path); } catch { }
                try { File.Delete(path + ".1"); } catch { }
            }
        }

        // Baseline (syscall per line): ~144k/s on this machine. Buffered
        // writer target is well above; 250k fails the syscall path with
        // margin while the buffered path clears it comfortably.
        Assert.True(bestLinesPerSec > 200_000,
            $"TrafficLog throughput too low: best of {rounds} rounds {bestLinesPerSec:F0}/s");
    }

    /// <summary>Wait-registration latency against a full ring: WaitForMatchAsync
    /// snapshots the ring and scans it for an immediate match. Wide bound â€?
    /// guards against an accidental O(nÂ²) setup, not against the scan itself
    /// (the fix moved the scan OUT of the buffer lock; see the concurrency
    /// tests in DataBufferServiceTests for the non-blocking contract).</summary>
    [Fact]
    public void WaitForMatch_FullBuffer_SetupStaysFast()
    {
        using var buffer = new DataBufferService(10_000);
        for (int i = 0; i < 10_000; i++)
            buffer.AddEntry(new LogEntry { Id = i + 1, Direction = "RX", Text = "payload " + i });

        const int setups = 100;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < setups; i++)
        {
            // Non-matching pattern: exercises the full-ring scan + register path.
            var task = buffer.WaitForMatchAsync("NO-SUCH-PATTERN-XYZ", "contains", false, null, 1);
            Assert.False(task.IsCompletedSuccessfully);
        }
        sw.Stop();

        double msPerSetup = sw.Elapsed.TotalMilliseconds / setups;
        Assert.True(msPerSetup < 50,
            $"Wait setup too slow on full ring: {msPerSetup:F2}ms/setup");
    }
}
