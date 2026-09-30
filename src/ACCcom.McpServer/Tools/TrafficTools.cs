using System.ComponentModel;
using ACCcom.Core.Services;
using ModelContextProtocol.Server;

namespace ACCcom.McpServer.Tools;

/// <summary>
/// Flight-recorder tools over the shared traffic mirror. Every MCP send/receive
/// lands in <see cref="McpTrafficLog"/> — including data already consumed from
/// the live buffer by read_data — so these tools answer "what DID happen on the
/// wire", while SerialTools' read_data/wait_for_response answer "what is here
/// now". The in-memory ring's seq is assigned by the writer, monotonic within
/// the server process, and never reset by rotation or external truncation,
/// which makes it a stable incremental cursor for clean-repro workflows.
/// </summary>
[McpServerToolType]
public class TrafficTools
{
    private readonly ToolContext _ctx;

    public TrafficTools(ToolContext ctx)
    {
        _ctx = ctx;
    }

    [McpServerTool, Description("Read the serial traffic flight recorder: every MCP send/receive is mirrored here, including data already consumed by read_data. Default returns the last `limit` entries; cursor mode returns entries after sinceSeq — note the returned lastSeq BEFORE your serial ops, then call again with sinceSeq to see exactly what happened (clean repro). Filters: direction (TX/RX/SYS), portTag, search (substring over text/hex/tool/port). compact=true merges text/hex into one payload field truncated to maxChars (token-friendly).")]
    public Task<string> TrafficLog(
        [Description("Return only entries after this seq (0 = tail mode, last `limit` entries)")] long sinceSeq = 0,
        [Description("Max entries to return (1-500, default 50)")] int limit = 50,
        [Description("Filter by direction: TX, RX, or SYS (port open/close events)")] string? direction = null,
        [Description("Filter by multi-port session tag")] string? portTag = null,
        [Description("Substring filter over text/hex/tool/portTag")] string? search = null,
        [Description("Merge text/hex into a single payload field (token-friendly)")] bool compact = false,
        [Description("Payload truncation for compact mode (1-4096, default 512)")] int maxChars = 512)
        => _ctx.Guard(() => Task.FromResult(TrafficLogCore(sinceSeq, limit, direction, portTag, search, compact, maxChars)));

    private string TrafficLogCore(long sinceSeq, int limit, string? direction, string? portTag, string? search, bool compact, int maxChars)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;
        if (maxChars < 1) maxChars = 1;   // 与面板 compact API 一致:允许极小值以便精确测试码点截断
        if (maxChars > 4096) maxChars = 4096;
        direction = NormalizeFilter(direction);
        portTag = string.IsNullOrWhiteSpace(portTag) ? null : portTag.Trim();
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var entries = _ctx.TrafficLog.ReadRecent(sinceSeq, limit, direction, portTag, search);
        var (lastSeq, kept) = _ctx.TrafficLog.RecentWatermark();

        // object 声明类型让 STJ 按运行时类型(匿名类型)序列化;两个投影形状
        // 不同,统一到 IEnumerable<object> 才能过编译。稀疏策略由 RawJson 的
        // JsonOpts 保证:null 列(tag)不出现。
        IEnumerable<object> projected;
        if (compact)
        {
            projected = entries.Select(e => (object)new
            {
                seq = e.Seq,
                dir = e.Direction,
                tool = e.Tool,
                tag = string.IsNullOrEmpty(e.PortTag) ? null : e.PortTag,
                ts = e.Timestamp,
                payload = TruncateCodePoints(
                    string.IsNullOrEmpty(e.Text) ? e.RawHex : e.Text, maxChars),
            });
        }
        else
        {
            projected = entries.Select(e => (object)new
            {
                seq = e.Seq,
                id = e.Id,
                tool = e.Tool,
                ts = e.Timestamp,
                dir = e.Direction,
                rawHex = e.RawHex,
                text = e.Text,
                tag = string.IsNullOrEmpty(e.PortTag) ? null : e.PortTag,
            });
        }

        // Sparse policy matches ToolContext.RawJson: null columns (tag echo)
        // never reach the wire.
        return _ctx.RawJson(new { success = true, data = new { lastSeq, kept, entries = projected.ToList() } });
    }

    private static string? NormalizeFilter(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) || string.Equals(v, "all", StringComparison.OrdinalIgnoreCase) ? null : v;
    }

    /// <summary>Codepoint-safe truncation: cutting between a surrogate pair
    /// would emit a lone surrogate, so back off one UTF-16 unit when the cut
    /// lands mid-pair.</summary>
    private static string TruncateCodePoints(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        var end = max;
        if (char.IsHighSurrogate(s[max - 1])) end = max - 1;
        return s.Substring(0, end);
    }
}
