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

            const int count = 5_000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
                serial.InjectRxData($"AA 55 {(i & 0xFF):X2}");
            sw.Stop();

            Assert.Equal(count + warmup, ctx.Buffer.Count());
            double entriesPerSec = count / sw.Elapsed.TotalSeconds;
            // Baseline (per-entry traffic-log syscall): ~68k/s on this machine.
            Assert.True(entriesPerSec > 100_000,
                $"MCP receive chain too slow: {entriesPerSec:F0}/s ({count} entries in {sw.Elapsed.TotalMilliseconds:F0}ms)");
        }
        finally
        {
            // Disposes the provider, which disposes the temp-file TrafficLog
            // (flushing buffered lines) �?keeps benchmark files out of temp.
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
