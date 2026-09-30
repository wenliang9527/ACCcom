using System.Text.Json;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

/// <summary>
/// traffic_log 工具契约:飞行记录仪(内存环)的增量游标、过滤、compact 投影。
/// 记录直接写入 ToolContextFactory 注入的临时文件 TrafficLog 实例(其内存环
/// 与生产共用同一段代码),不触碰真实 mcp-traffic.jsonl。
/// </summary>
public class TrafficToolsTests
{
    private static JsonElement Data(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task TrafficLog_EmptyRing_ReturnsSuccess_WithZeroWatermark()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new TrafficTools(ctx);
            var result = await tools.TrafficLog();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            var data = Data(result);
            Assert.Equal(0, data.GetProperty("lastSeq").GetInt64());
            Assert.Equal(0, data.GetProperty("kept").GetInt32());
            Assert.Empty(data.GetProperty("entries").EnumerateArray());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task TrafficLog_FullShape_ProjectsAllFields_AndSparseTag()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            ctx.TrafficLog.Record(7, "send", "TX", "53 48", "SH", "");
            ctx.TrafficLog.Record(0, "rx", "RX", "00", "ok", "p1");
            var tools = new TrafficTools(ctx);
            var result = await tools.TrafficLog();
            var data = Data(result);
            Assert.Equal(2, data.GetProperty("lastSeq").GetInt64());
            var entries = data.GetProperty("entries");
            Assert.Equal(2, entries.GetArrayLength());

            var first = entries[0];
            Assert.Equal(1, first.GetProperty("seq").GetInt64());
            Assert.Equal(7, first.GetProperty("id").GetInt32());
            Assert.Equal("send", first.GetProperty("tool").GetString());
            Assert.Equal("TX", first.GetProperty("dir").GetString());
            Assert.Equal("53 48", first.GetProperty("rawHex").GetString());
            Assert.Equal("SH", first.GetProperty("text").GetString());
            Assert.False(first.TryGetProperty("tag", out _)); // 空 tag 稀疏省略

            Assert.Equal("p1", entries[1].GetProperty("tag").GetString());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task TrafficLog_CursorMode_ReturnsOnlyEntriesAfterSince()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            for (var i = 1; i <= 5; i++) ctx.TrafficLog.Record(0, "send", "TX", "", "m" + i);
            var tools = new TrafficTools(ctx);

            var data = Data(await tools.TrafficLog(sinceSeq: 3));
            var seqs = data.GetProperty("entries").EnumerateArray()
                .Select(e => e.GetProperty("seq").GetInt64()).ToArray();
            Assert.Equal(new long[] { 4, 5 }, seqs);

            // 干净复现收口:水位之后无新数据 → 空 entries,lastSeq 仍可锚定
            var tail = Data(await tools.TrafficLog(sinceSeq: 5));
            Assert.Empty(tail.GetProperty("entries").EnumerateArray());
            Assert.Equal(5, tail.GetProperty("lastSeq").GetInt64());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task TrafficLog_Filters_ByDirectionPortTagAndSearch()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            ctx.TrafficLog.Record(0, "send", "TX", "53 48", "HELLO");
            ctx.TrafficLog.Record(0, "rx", "RX", "00", "world", "p1");
            ctx.TrafficLog.Record(0, "open_port", "SYS", "", "COM15");
            var tools = new TrafficTools(ctx);

            var rx = Data(await tools.TrafficLog(direction: "rx"));
            Assert.Equal(1, rx.GetProperty("entries").GetArrayLength());

            var sys = Data(await tools.TrafficLog(direction: "SYS"));
            Assert.Equal("open_port", sys.GetProperty("entries")[0].GetProperty("tool").GetString());

            var byTag = Data(await tools.TrafficLog(portTag: "p1"));
            Assert.Equal(1, byTag.GetProperty("entries").GetArrayLength());

            var byHex = Data(await tools.TrafficLog(search: "53 48"));
            Assert.Equal(1, byHex.GetProperty("entries").GetArrayLength());

            var none = Data(await tools.TrafficLog(search: "nomatch"));
            Assert.Empty(none.GetProperty("entries").EnumerateArray());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task TrafficLog_Compact_MergesPayload_TruncatesCodepointSafe()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            ctx.TrafficLog.Record(0, "send", "TX", "53 48", "HELLO");
            ctx.TrafficLog.Record(0, "rx", "RX", "DE AD BE EF", ""); // text 空 → hex 回落
            ctx.TrafficLog.Record(0, "rx", "RX", "", "😀😀😀");      // 6 个 UTF-16 单元
            var tools = new TrafficTools(ctx);

            // maxChars=11:三行载荷(5/11/6 字符)都在限内 → 原样透出
            var data = Data(await tools.TrafficLog(compact: true, maxChars: 11));
            var entries = data.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal("HELLO", entries[0].GetProperty("payload").GetString());
            Assert.Equal("DE AD BE EF", entries[1].GetProperty("payload").GetString());
            Assert.Equal("😀😀😀", entries[2].GetProperty("payload").GetString());
            Assert.False(entries[0].TryGetProperty("rawHex", out _)); // compact 不带双份载荷

            // maxChars=5:第三个 emoji 在第 5 个 UTF-16 单元处被劈开高代理 → 回退到 2 个完整 emoji
            var t5 = Data(await tools.TrafficLog(compact: true, maxChars: 5));
            Assert.Equal("😀😀", t5.GetProperty("entries").EnumerateArray().ToArray()[2].GetProperty("payload").GetString());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task TrafficLog_LimitClamped_ToBounds()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            for (var i = 1; i <= 10; i++) ctx.TrafficLog.Record(0, "send", "TX", "", "m" + i);
            var tools = new TrafficTools(ctx);

            Assert.Equal(3, Data(await tools.TrafficLog(limit: 3)).GetProperty("entries").GetArrayLength());
            // limit 超上界钳到 500(此处只有 10 条,验证不炸且全返回)
            Assert.Equal(10, Data(await tools.TrafficLog(limit: 99999)).GetProperty("entries").GetArrayLength());
            // limit 非正 → 钳到 1
            Assert.Equal(1, Data(await tools.TrafficLog(limit: 0)).GetProperty("entries").GetArrayLength());
        }
        finally { sp.Dispose(); }
    }
}
