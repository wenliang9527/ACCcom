using System.Diagnostics;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// Hot-path throughput for the MCP receive chain: serial entry �?/// ToolContext handler �?DataBufferService.AddEntry + McpTrafficLog.Record.
/// Class name deliberately contains "RxHotPathBenchmarkTests" so the CI
/// coverage filter (FullyQualifiedName!~RxHotPathBenchmarkTests) excludes it
/// from coverlet instrumentation, matching the Core benchmark class.
/// Assertions use wide lower bounds (10x+ below the buffered path) so slow CI
/// never flakes while an fsync-per-line regression fails by an order of
/// magnitude. Never run under coverage collection.
/// </summary>
public class McpRxHotPathBenchmarkTests
{
    [Fact]
    public void ToolContext_ReceiveChain_SustainsHighThroughput()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var serial = (VirtualSerialService)ctx.Serial;
            serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

            // Warmup: first-run JIT of the handler/record paths otherwise
            // counts against the timed window and dips under the bound on a
            // cold build.
            const int warmup = 500;
            for (int i = 0; i < warmup; i++)
                serial.InjectRxData("AA 55 00");

            // Best of three rounds — same rationale as the Core Record
            // benchmark: the timed window is short, so one scheduler
            // preemption under parallel-suite load can sink a single round
            // below the bound without any real regression.
            const int count = 20_000;   // ~240ms/轮:并行抢占的影响降到毫秒级
            const int rounds = 3;
            var bestEntriesPerSec = 0.0;
            for (int round = 0; round < rounds; round++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    serial.InjectRxData($"AA 55 {(i & 0xFF):X2}");
                sw.Stop();
                double entriesPerSec = count / sw.Elapsed.TotalSeconds;
                if (entriesPerSec > bestEntriesPerSec)
                    bestEntriesPerSec = entriesPerSec;
            }

            // 数据完整性:缓冲确实收到了数据(DataBufferService 默认容量 10000,
            // 注入量超过它会封顶,所以只断言>0 而非特定条数——本测试的成立性
            // 由吞吐断言保证,完整条数归属 TrafficToolsTests)。
            Assert.True(ctx.Buffer.Count() > 0,
                $"buffer should hold received entries, got {ctx.Buffer.Count()}");
            // Baseline (per-entry traffic-log syscall): ~68k/s on this machine.
            // 缓冲路径实测 ~150k/s;120k 同时压过 syscall 路径、给并行裕量留 margin。
            Assert.True(bestEntriesPerSec > 120_000,
                $"MCP receive chain too slow: {bestEntriesPerSec:F0}/s (best of {rounds} rounds)");
        }
        finally
        {
            // Disposes the provider, which disposes the temp-file TrafficLog
            // (flushing buffered lines) — keeps benchmark files out of temp.
            sp.Dispose();
        }
    }

    [Fact]
    public void ToolContext_ReceiveChain_TrafficLogRecordsOneLinePerEntry()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        string path = ToolContextFactory.TrafficLogPath(sp);
        try
        {
            var serial = (VirtualSerialService)ctx.Serial;
            serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });
            serial.InjectRxData("AA 55 01");
        }
        finally { sp.Dispose(); }

        var lines = File.ReadAllLines(path);
        var line = Assert.Single(lines);
        Assert.Contains("\"tool\":\"rx\"", line);
        Assert.Contains("\"direction\":\"RX\"", line);
    }

    [Fact]
    public async Task ReadData_100Entries_SustainsPollThroughput()
    {
        // End-to-end agent polling cost: buffer read + lean projection +
        // JSON response serialization, the per-call hot path for cursor polls.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new Tools.SerialTools(ctx);
            for (int i = 0; i < 100; i++)
            {
                ctx.Buffer.AddEntry(new LogEntry
                {
                    Id = i + 1,
                    Direction = "RX",
                    Text = $"payload-{i} status=OK temp=25.3",
                    RawHex = "AA 55 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E"
                });
            }

            // Warmup: JIT + serializer metadata caches.
            for (int i = 0; i < 50; i++)
                await tools.ReadData(limit: 100);

            const int iterations = 500;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
                await tools.ReadData(limit: 100);
            sw.Stop();

            double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
            double usPerOp = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
            // Measured: ~238µs/op with reflection, ~130-180µs/op with the
            // source-generated writer (4.2k-7.7k ops/s) — too close for a
            // discriminating bound on shared CI. Keep a wide bound that only
            // catches order-of-magnitude regressions (e.g. re-serializing the
            // whole response per entry, or a sync file write in the poll path).
            Assert.True(opsPerSec > 2_000,
                $"read_data poll too slow: {opsPerSec:F0}/s ({usPerOp:F0}µs/op)");
        }
        finally { sp.Dispose(); }
    }
}
