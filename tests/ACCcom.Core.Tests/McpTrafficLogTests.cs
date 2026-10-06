using System.Text.Json;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class McpTrafficLogTests
{
    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"mcp-traffic-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public async Task Record_AfterExternalTruncate_ReopensCleanly_WithoutSparseHole()
    {
        // The GUI's Clear truncates the JSONL behind the writer's append handle.
        // Without the shrink check the next flush wrote at the stale offset,
        // leaving a sparse hole of torn bytes; with it the writer reopens fresh
        // and the cleared history (plus its in-flight buffer) stays cleared.
        var path = TempPath();
        try
        {
            using (var log = new McpTrafficLog(path))
            {
                log.Record(0, "send", "TX", "", "pre-clear-line");
                // One flush interval guaranteed: the pre-clear line reaches the
                // file and the stream position advances past zero.
                await Task.Delay(300);

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    fs.SetLength(0);

                log.Record(0, "send", "TX", "", "mid-clear-line");
                // Next tick detects the shrink, reopens fresh (dropping the
                // mid-clear line with the cleared history).
                await Task.Delay(400);
                log.Record(0, "send", "TX", "", "post-clear-line");
                await Task.Delay(400);
            }

            var content = File.ReadAllText(path);
            Assert.DoesNotContain("pre-clear-line", content);
            Assert.DoesNotContain("mid-clear-line", content);
            Assert.Contains("post-clear-line", content);
            Assert.DoesNotContain('\0', content); // no sparse hole of torn bytes
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Record_WritesJsonlLine_WithToolAndDirection()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "48 45 4C 4C 4F", "HELLO");
        }

        var line = File.ReadAllLines(path).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("send", root.GetProperty("tool").GetString());
        Assert.Equal("TX", root.GetProperty("direction").GetString());
        Assert.Equal("48 45 4C 4C 4F", root.GetProperty("rawHex").GetString());
        Assert.Equal("HELLO", root.GetProperty("text").GetString());
        Assert.True(root.TryGetProperty("timestamp", out _));
        Assert.Equal("", root.GetProperty("portTag").GetString());
    }

    [Fact]
    public void Record_WithTag_WritesPortTagField()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "AA", "a", "sensor_a");
        }

        var line = File.ReadAllLines(path).Single();
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("sensor_a", doc.RootElement.GetProperty("portTag").GetString());
    }

    [Fact]
    public void Record_AppendsMultipleLines_InOrder()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "send", "TX", "AA", "a");
            log.Record(2, "send", "TX", "BB", "b");
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"rawHex\":\"AA\"", lines[0]);
        Assert.Contains("\"rawHex\":\"BB\"", lines[1]);
    }

    [Fact]
    public void Record_NullText_StillWrites()
    {
        var path = TempPath();
        using (var log = new McpTrafficLog(path))
        {
            log.Record(1, "read", "RX", "01 02", "");
        }

        var lines = File.ReadAllLines(path);
        var line = Assert.Single(lines);
        Assert.Contains("\"text\":\"\"", line);
    }

    [Fact]
    public void Rotation_Respects_MaxLines_And_Uses_InstancePath()
    {
        var path = TempPath();
        var oldMax = McpTrafficLog.MaxLines;
        try
        {
            McpTrafficLog.MaxLines = 3;
            using (var log = new McpTrafficLog(path))
            {
                log.Record(1, "send", "TX", "AA", "a");
                log.Record(2, "send", "TX", "BB", "b");
                log.Record(3, "send", "TX", "CC", "c"); // hits MaxLines → rotate
            }

            // After rotation the main file is fresh (0-1 lines), the old content
            // moved to the .1 backup.
            var mainLines = File.ReadAllLines(path);
            var backupLines = File.ReadAllLines(path + ".1");
            Assert.True(mainLines.Length <= 1, $"main should be fresh, got {mainLines.Length}");
            Assert.Equal(3, backupLines.Length);
        }
        finally
        {
            McpTrafficLog.MaxLines = oldMax;
        }
    }

    // ---- v1.7 进程内最近流量环(traffic_log MCP 工具数据源) ----

    [Fact]
    public void ReadRecent_TailMode_ReturnsLastLimit_WithWatermark()
    {
        var path = TempPath();
        using var log = new McpTrafficLog(path, ringCapacity: 100);
        for (var i = 1; i <= 7; i++) log.Record(0, "send", "TX", "", "m" + i);

        var (lastSeq, kept) = log.RecentWatermark();
        Assert.Equal(7, lastSeq);
        Assert.Equal(7, kept);

        var page = log.ReadRecent(0, 3);
        Assert.Equal(new long[] { 5, 6, 7 }, page.Select(e => e.Seq));
        Assert.All(page, e => Assert.Equal("m" + e.Seq, e.Text));
    }

    [Fact]
    public void ReadRecent_SinceCursor_ReturnsEntriesAfter_AndEmptyAtEnd()
    {
        var path = TempPath();
        using var log = new McpTrafficLog(path, ringCapacity: 100);
        for (var i = 1; i <= 7; i++) log.Record(0, "send", "TX", "", "m" + i);

        Assert.Equal(new long[] { 6, 7 }, log.ReadRecent(5, 50).Select(e => e.Seq));
        Assert.Empty(log.ReadRecent(7, 50));
        Assert.Empty(log.ReadRecent(99, 50)); // 游标超前水位
    }

    [Fact]
    public void ReadRecent_SinceBehindWindow_FallsBackToTailLimit()
    {
        var path = TempPath();
        using var log = new McpTrafficLog(path, ringCapacity: 5);
        for (var i = 1; i <= 12; i++) log.Record(0, "send", "TX", "", "m" + i);

        var (_, kept) = log.RecentWatermark();
        Assert.Equal(5, kept); // 环裁剪到容量
        var page = log.ReadRecent(3, 50); // 游标早于窗口
        Assert.Equal(new long[] { 8, 9, 10, 11, 12 }, page.Select(e => e.Seq));
    }

    [Fact]
    public void ReadRecent_RingTrims_InBulk_ToCapacity()
    {
        var path = TempPath();
        using var log = new McpTrafficLog(path, ringCapacity: 5); // slack=0(容量<256)
        for (var i = 1; i <= 12; i++) log.Record(0, "send", "TX", "", "m" + i);
        Assert.Equal(new long[] { 8, 9, 10, 11, 12 }, log.ReadRecent(0, 50).Select(e => e.Seq));
    }

    [Fact]
    public void ReadRecent_Filters_ByDirectionAndSearch()
    {
        var path = TempPath();
        using var log = new McpTrafficLog(path, ringCapacity: 100);
        log.Record(0, "send", "TX", "53 48", "HELLO");
        log.Record(0, "rx", "RX", "00", "world", "p1");
        log.Record(0, "open_port", "SYS", "", "COM15");

        Assert.Equal(new long[] { 2 }, log.ReadRecent(0, 50, direction: "rx").Select(e => e.Seq));
        Assert.Equal(3, log.ReadRecent(0, 50).Count); // 不过滤全返回
        Assert.Equal(new long[] { 1 }, log.ReadRecent(0, 50, search: "hello").Select(e => e.Seq));
        Assert.Equal(new long[] { 1 }, log.ReadRecent(0, 50, search: "53 48").Select(e => e.Seq)); // 命中 hex
        Assert.Equal(new long[] { 2 }, log.ReadRecent(0, 50, portTag: "p1").Select(e => e.Seq));
        // portTag 过滤大小写不敏感,与 direction/search 及 DataBufferService 对齐:
        // tag "COM1" vs 过滤词 "com1" 不该静默过滤成空。
        Assert.Equal(new long[] { 2 }, log.ReadRecent(0, 50, portTag: "P1").Select(e => e.Seq));
        Assert.Empty(log.ReadRecent(0, 50, search: "nomatch"));
    }

    [Fact]
    public async Task Ring_Clears_OnExternalTruncate_But_Seq_Stays_Monotonic()
    {
        var path = TempPath();
        try
        {
            using (var log = new McpTrafficLog(path, ringCapacity: 100))
            {
                log.Record(0, "send", "TX", "", "pre-clear-1");
                log.Record(0, "send", "TX", "", "pre-clear-2");
                await Task.Delay(300);

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    fs.SetLength(0);

                log.Record(0, "send", "TX", "", "post-clear-1");
                await Task.Delay(400);
                log.Record(0, "send", "TX", "", "post-clear-2");

                // 收缩检测在 flush tick 上异步发生,并行负载下 tick 可能迟到:
                // 条件轮询到环里不再有 pre-clear 条目为止(与 R44/R53 同一模式,
                // 不对 tick 的确切触发时刻做假设)。tick 在"截断前/后"触发决定
                // post-clear-1 是否也随缓冲一起被丢弃,因此 kept 允许 {3,4} 或 {4}。
                var deadline = DateTime.UtcNow.AddSeconds(5);
                IReadOnlyList<TrafficEntry> page;
                while (true)
                {
                    page = log.ReadRecent(0, 50);
                    if (!page.Any(e => e.Text.StartsWith("pre-clear", StringComparison.Ordinal))) break;
                    if (DateTime.UtcNow > deadline)
                        throw new InvalidOperationException("ring never cleared after external truncate");
                    await Task.Delay(50);
                }

                var (lastSeq, kept) = log.RecentWatermark();
                Assert.Equal(4, lastSeq); // seq 不因截断重置(游标语义)
                Assert.True(kept is 1 or 2, $"kept should be 1 or 2, got {kept}");
                Assert.DoesNotContain(page, e => e.Text.StartsWith("pre-clear", StringComparison.Ordinal));
                Assert.All(page, e => Assert.StartsWith("post-clear", e.Text, StringComparison.Ordinal));
                Assert.Equal(page.Select(e => e.Seq).Max(), lastSeq); // 最后一条 = 水位
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
