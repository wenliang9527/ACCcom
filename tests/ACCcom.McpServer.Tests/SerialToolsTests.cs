using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.McpServer.Tests.TestHelpers;
using ACCcom.McpServer.Tools;

namespace ACCcom.McpServer.Tests;

public class SerialToolsTests
{
    [Fact]
    public async Task ListPorts_ReturnsSuccessJson()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ListPorts();
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task OpenPort_RequiresPortName()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.OpenPort("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("required", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Send_RejectsEmptyData()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.Send("");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClearBuffer_ReturnsSuccess()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClearBuffer("all");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_ReturnsEmptyArray_WhenBufferEmpty()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ReadData(0, 10, null);
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_NegativeLimitAndSinceId_StillSucceeds()
    {
        // Negative limit is "no limit" and a negative sinceId is "from the
        // start" in the buffer — the tool must succeed, not throw or fail.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ReadData(-5, -1, null);
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_LatestId_DoesNotSkipEntriesWithLaggingIds()
    {
        // Entry ids come from independent RX/TX counters, so RX can run far
        // ahead of TX. latestId must be the buffer's arrival cursor — advancing
        // by Entry.Id (as the old Math.Max formula did) hid every TX row whose
        // id lagged the RX cursor.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            ctx.Buffer.AddEntry(new LogEntry { Id = 50, Direction = "RX", Text = "rx" });
            var tools = new SerialTools(ctx);

            var first = await tools.ReadData(0, 10, null);
            Assert.True(ToolContextFactory.ExtractSuccess(first));
            var firstLatestId = LatestIdOf(first);

            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "TX", Text = "tx" });
            var second = await tools.ReadData(firstLatestId, 10, null);

            Assert.True(ToolContextFactory.ExtractSuccess(second));
            Assert.Contains("\"tx\"", second);
        }
        finally { sp.Dispose(); }
    }

    private static int LatestIdOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("latestId").GetInt32();
    }

    [Fact]
    public async Task WaitForResponse_RequiresPattern()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForResponse("", 100, "contains");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClosePort_NotOpen_ReportsPortNotOpen()
    {
        // Honest close: "nothing to close" must be distinguishable from a real
        // close, so an agent never believes it closed a port that was never open.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClosePort();
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_RejectsEmptyData()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.SendAndWait("", "OK");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Send_InvalidHex_ReturnsPreciseError()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");

            var result = await tools.Send("ZZ ZZ", isHex: true);
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("Invalid hex", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_InvalidHex_ReturnsPreciseError()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");

            var result = await tools.SendAndWait("ZZ ZZ", "OK", isHex: true);
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Contains("Invalid hex", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Send_WritesExactlyOneTxTrafficLine_WithSendTool()
    {
        // Regression guard: the receive handler used to mirror TX entries as
        // tool="rx" as well, so every send produced two JSONL lines (the first
        // mislabeled) and doubled the traffic-log I/O.
        var (ctx, sp) = ToolContextFactory.Create();
        string path = ToolContextFactory.TrafficLogPath(sp);
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");
            var result = await tools.Send("AT");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); } // flushes buffered traffic lines

        var txLines = File.ReadAllLines(path)
            .Where(l => l.Contains("\"direction\":\"TX\"", StringComparison.Ordinal))
            .ToList();
        var line = Assert.Single(txLines);
        Assert.Contains("\"tool\":\"send\"", line);
    }

    [Fact]
    public async Task SendAndWait_WritesExactlyOneTxTrafficLine_WithSendAndWaitTool()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        string path = ToolContextFactory.TrafficLogPath(sp);
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");
            // No echo on the virtual port: the wait times out (min clamp 100ms),
            // but the TX traffic line is written right after the send.
            await tools.SendAndWait("AT", "OK", timeoutMs: 100);
        }
        finally { sp.Dispose(); }

        var txLines = File.ReadAllLines(path)
            .Where(l => l.Contains("\"direction\":\"TX\"", StringComparison.Ordinal))
            .ToList();
        var line = Assert.Single(txLines);
        Assert.Contains("\"tool\":\"send_and_wait\"", line);
    }

    // ── read_data: tail cursor mode + lean projection + maxLength ──

    [Fact]
    public async Task ReadData_tail_returns_newest_entries_in_arrival_order_without_cursor()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "m1" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "RX", Text = "m2" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 3, Direction = "RX", Text = "m3" });

            var result = await tools.ReadData(tail: 2);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var entries = doc.RootElement.GetProperty("data").GetProperty("entries");
            Assert.Equal(2, entries.GetArrayLength());
            Assert.Equal("m2", entries[0].GetProperty("text").GetString());
            Assert.Equal("m3", entries[1].GetProperty("text").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("data").GetProperty("latestId").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_tail_direction_filter_keeps_only_matching_rows()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "TX", Text = "t1" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "RX", Text = "r1" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 3, Direction = "TX", Text = "t2" });

            var result = await tools.ReadData(direction: "TX", tail: 1);

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("data").GetProperty("entries")[0];
            Assert.Equal("t2", entry.GetProperty("text").GetString());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_tail_cursor_hands_off_to_incremental_poll_without_gap_or_duplicate()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "m1" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "RX", Text = "m2" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 3, Direction = "RX", Text = "m3" });

            var tailResult = await tools.ReadData(tail: 1);
            var cursor = LatestIdOf(tailResult);

            var caughtUp = await tools.ReadData(cursor, 10, null);
            using (var doc = JsonDocument.Parse(caughtUp))
                Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());

            ctx.Buffer.AddEntry(new LogEntry { Id = 4, Direction = "RX", Text = "m4" });
            var next = await tools.ReadData(cursor, 10, null);

            using var doc2 = JsonDocument.Parse(next);
            var entries = doc2.RootElement.GetProperty("data").GetProperty("entries");
            Assert.Equal(1, entries.GetArrayLength());
            Assert.Equal("m4", entries[0].GetProperty("text").GetString());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_maxLength_truncates_long_fields_and_flags_them()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "ABCDEFGHIJ", RawHex = "41 42 43 44 45 46 47 48 49 4A" });
            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "RX", Text = "ok" });

            var result = await tools.ReadData(maxLength: 4);

            using var doc = JsonDocument.Parse(result);
            var entries = doc.RootElement.GetProperty("data").GetProperty("entries");

            var longEntry = entries[0];
            Assert.Equal("ABCD…", longEntry.GetProperty("text").GetString());
            Assert.True(longEntry.GetProperty("truncated").GetBoolean());

            var shortEntry = entries[1];
            Assert.Equal("ok", shortEntry.GetProperty("text").GetString());
            // Sparse policy: truncated=false is the default — the column is omitted.
            Assert.False(shortEntry.TryGetProperty("truncated", out _));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_entries_omit_ui_only_fields()
    {
        // LogEntry carries UI state (highlight color, search match, field
        // annotations) that is irrelevant to MCP clients — the lean projection
        // must not serialize it.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry
            {
                Id = 1,
                Direction = "RX",
                Text = "row",
                HighlightColor = "#FF0000",
                IsSearchMatch = true,
                Fields = new List<FieldAnnotation>()
            });

            var result = await tools.ReadData();

            Assert.DoesNotContain("highlightColor", result, StringComparison.Ordinal);
            Assert.DoesNotContain("isSearchMatch", result, StringComparison.Ordinal);
            Assert.DoesNotContain("\"fields\"", result, StringComparison.Ordinal);
            // Sparse policy: no maxLength → nothing truncated → column omitted.
            Assert.DoesNotContain("\"truncated\"", result, StringComparison.Ordinal);
            // Sparse policy: entry has no port tag → column omitted.
            Assert.DoesNotContain("\"portTag\"", result, StringComparison.Ordinal);
            Assert.Contains("\"row\"", result, StringComparison.Ordinal);
        }
        finally { sp.Dispose(); }
    }

    // ── wait_for_quiet ──

    [Fact]
    public async Task WaitForQuiet_silent_buffer_returns_quiet_true()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var result = await tools.WaitForQuiet(quietMs: 50, timeoutMs: 5000);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("quiet").GetBoolean());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForQuiet_timeout_shorter_than_quiet_window_returns_quiet_false()
    {
        // quietMs (200) > timeoutMs (100): the timeout must win on a silent
        // buffer, reporting quiet=false instead of hanging or lying.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var result = await tools.WaitForQuiet(quietMs: 200, timeoutMs: 100);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            Assert.False(doc.RootElement.GetProperty("data").GetProperty("quiet").GetBoolean());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForQuiet_incoming_data_extends_the_quiet_window()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var wait = tools.WaitForQuiet(quietMs: 200, timeoutMs: 5000);
            await Task.Delay(80);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "late" });

            var result = await wait;

            using var doc = JsonDocument.Parse(result);
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("quiet").GetBoolean());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForQuiet_watches_the_tagged_buffer_not_the_default()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            // A tagged session must exist for the wait — the tag guard requires
            // an open port, so open one before watching its buffer.
            await tools.OpenPort("COM10", tag: "sensor");
            // Continuous traffic on the default buffer must not prevent the
            // tagged buffer from reporting quiet.
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "main-noise" });

            var result = await tools.WaitForQuiet(quietMs: 50, timeoutMs: 300, tag: "sensor");

            using var doc = JsonDocument.Parse(result);
            Assert.True(doc.RootElement.GetProperty("data").GetProperty("quiet").GetBoolean());
        }
        finally { sp.Dispose(); }
    }

    // ── read_data waitMs long-poll ──

    [Fact]
    public async Task ReadData_waitMs_returns_immediately_when_cursor_has_data()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "ready" });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await tools.ReadData(waitMs: 5000);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 2000, $"waited {sw.ElapsedMilliseconds}ms despite buffered data");
            using var doc = JsonDocument.Parse(result);
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_waitMs_returns_as_soon_as_data_arrives()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wait = tools.ReadData(sinceId: 0, waitMs: 5000);
            await Task.Delay(80);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "late arrival" });
            var result = await wait;
            sw.Stop();

            using var doc = JsonDocument.Parse(result);
            var entries = doc.RootElement.GetProperty("data").GetProperty("entries");
            Assert.Equal(1, entries.GetArrayLength());
            Assert.Equal("late arrival", entries[0].GetProperty("text").GetString());
            // Event-driven wake-up: must come back far before the 5s timeout
            // (a lost-wakeup bug would burn the full wait).
            Assert.True(sw.ElapsedMilliseconds < 4000, $"took {sw.ElapsedMilliseconds}ms");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_waitMs_timeout_returns_empty_promptly_after_wait()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await tools.ReadData(waitMs: 300);
            sw.Stop();

            using var doc = JsonDocument.Parse(result);
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("latestId").GetInt32());
            Assert.True(sw.ElapsedMilliseconds >= 250, $"returned after {sw.ElapsedMilliseconds}ms — wait not honored");
            Assert.True(sw.ElapsedMilliseconds < 3000, $"took {sw.ElapsedMilliseconds}ms");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_waitMs_direction_filter_returns_promptly_with_advanced_cursor()
    {
        // Traffic the filter excludes must still end the wait (the cursor
        // advances), not park until timeout — otherwise a filtered long-poll
        // spins forever on skipped traffic.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var wait = tools.ReadData(sinceId: 0, direction: "RX", waitMs: 5000);
            await Task.Delay(80);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "TX", Text = "excluded" });
            var result = await wait;
            sw.Stop();

            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal(0, data.GetProperty("entries").GetArrayLength());
            Assert.True(data.GetProperty("latestId").GetInt32() > 0, "cursor did not advance past filtered traffic");
            Assert.True(sw.ElapsedMilliseconds < 4000, $"took {sw.ElapsedMilliseconds}ms");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_waitMs_is_ignored_in_tail_mode()
    {
        // tail > 0 takes precedence over the cursor path entirely — waitMs
        // must not park an empty tail read.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await tools.ReadData(tail: 5, waitMs: 5000);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 2000, $"tail read waited {sw.ElapsedMilliseconds}ms");
            using var doc = JsonDocument.Parse(result);
            Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());
        }
        finally { sp.Dispose(); }
    }

    // ── round 3: token saving + standardization ──

    [Fact]
    public async Task ReadData_timestamp_is_iso8601_millisecond_precision()
    {
        // One fixed format for every timestamp: date + ms fraction + kind
        // offset, never the 7-digit round-trip form.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "ts" });

            var result = await tools.ReadData();

            using var doc = JsonDocument.Parse(result);
            var ts = doc.RootElement.GetProperty("data").GetProperty("entries")[0]
                .GetProperty("timestamp").GetString()!;
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}([zZ]|[+-]\d{2}:\d{2})?$", ts);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_fields_text_omits_hex_and_unselected_columns()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "OK", RawHex = "4F 4B" });

            var result = await tools.ReadData(fields: "text");

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("data").GetProperty("entries")[0];
            Assert.Equal("OK", entry.GetProperty("text").GetString());
            Assert.False(entry.TryGetProperty("hex", out _));
            Assert.False(entry.TryGetProperty("id", out _));
            Assert.False(entry.TryGetProperty("timestamp", out _));
            Assert.False(entry.TryGetProperty("direction", out _));
            Assert.False(entry.TryGetProperty("portTag", out _));
            Assert.False(entry.TryGetProperty("truncated", out _));
            // Envelope columns are not part of the fields mask.
            Assert.Equal(1, doc.RootElement.GetProperty("data").GetProperty("latestId").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_fields_multi_select_returns_only_those_columns()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 7, Direction = "RX", Text = "OK", RawHex = "4F 4B" });

            var result = await tools.ReadData(fields: "direction, text ,hex");

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("data").GetProperty("entries")[0];
            Assert.Equal("RX", entry.GetProperty("direction").GetString());
            Assert.Equal("OK", entry.GetProperty("text").GetString());
            Assert.Equal("4F 4B", entry.GetProperty("hex").GetString());
            Assert.False(entry.TryGetProperty("id", out _));
            Assert.False(entry.TryGetProperty("timestamp", out _));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_fields_unknown_column_returns_structured_error()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "x" });

            var result = await tools.ReadData(fields: "text,payload");

            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INVALID_FIELDS", ToolContextFactory.ExtractErrorCode(result));
            Assert.Contains("payload", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_default_fields_still_returns_all_columns()
    {
        // fields=null must stay backward compatible: full column set.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 3, Direction = "RX", Text = "full", RawHex = "4F 4B" });

            var result = await tools.ReadData();

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("data").GetProperty("entries")[0];
            foreach (var col in new[] { "id", "timestamp", "direction", "text", "hex" })
                Assert.True(entry.TryGetProperty(col, out _), $"missing column {col}");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Send_response_does_not_echo_payload()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");

            var result = await tools.Send("AT+GMR");

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.False(data.TryGetProperty("sent", out _), "request payload must not be echoed");
            Assert.Equal(6, data.GetProperty("byteLength").GetInt32());
            Assert.False(data.GetProperty("isHex").GetBoolean());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task Errors_carry_structured_code_and_message()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);

            var notOpen = await tools.Send("HI", tag: "ghost");
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(notOpen));
            Assert.Contains("ghost", ToolContextFactory.ExtractError(notOpen) ?? "");

            await tools.OpenPort("COM10");
            var badHex = await tools.Send("ZZ", isHex: true);
            Assert.Equal("INVALID_HEX", ToolContextFactory.ExtractErrorCode(badHex));
            Assert.Contains("Invalid hex", ToolContextFactory.ExtractError(badHex) ?? "");

            var empty = await tools.Send("");
            Assert.Equal("EMPTY_DATA", ToolContextFactory.ExtractErrorCode(empty));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_returns_latestId_cursor()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "HELLO" });

            var result = await tools.WaitForResponse("NEVER-MATCHES", timeoutMs: 200);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("matched").GetBoolean());
            Assert.Equal(1, data.GetProperty("latestId").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForQuiet_returns_latestId_cursor()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "done" });

            var result = await tools.WaitForQuiet(quietMs: 50, timeoutMs: 2000);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("quiet").GetBoolean());
            Assert.Equal(1, data.GetProperty("latestId").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_returns_latestId_after_immediate_match()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "READY" });

            var result = await tools.SendAndWait("GO", "READY", timeoutMs: 2000);

            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("matched").GetBoolean());
            // Two buffered entries: the pre-seeded RX ("READY", seq 1) plus the
            // TX echo of the send itself (seq 2) — the cursor must cover both.
            Assert.Equal(2, data.GetProperty("latestId").GetInt32());
            Assert.False(data.TryGetProperty("sent", out _), "request payload must not be echoed");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitCursor_hands_off_to_read_data_without_gap()
    {
        // The latestId returned by a wait tool must be directly usable as
        // sinceId — no re-read of what the wait already covered, no skip.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = "first" });
            var quiet = await tools.WaitForQuiet(quietMs: 50, timeoutMs: 2000);
            int cursor;
            using (var doc = JsonDocument.Parse(quiet))
                cursor = doc.RootElement.GetProperty("data").GetProperty("latestId").GetInt32();

            var caughtUp = await tools.ReadData(cursor);
            using (var doc = JsonDocument.Parse(caughtUp))
                Assert.Equal(0, doc.RootElement.GetProperty("data").GetProperty("entries").GetArrayLength());

            ctx.Buffer.AddEntry(new LogEntry { Id = 2, Direction = "RX", Text = "second" });
            var next = await tools.ReadData(cursor);
            using var doc2 = JsonDocument.Parse(next);
            var entries = doc2.RootElement.GetProperty("data").GetProperty("entries");
            Assert.Equal(1, entries.GetArrayLength());
            Assert.Equal("second", entries[0].GetProperty("text").GetString());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task DefaultSessionResponses_omit_empty_tag_echo()
    {
        // Sparse envelope policy must hold across EVERY tool, not just
        // read_data: an empty tag echo is dead weight on the default session.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var results = new[]
            {
                await tools.OpenPort("COM10"),
                await tools.Send("Hi"),
                await tools.WaitForQuiet(quietMs: 50, timeoutMs: 2000),
                await tools.ReadData(),
                await tools.WaitForResponse("NEVER-MATCHES", timeoutMs: 200),
                await tools.ClearBuffer()
            };

            foreach (var r in results)
            {
                Assert.True(ToolContextFactory.ExtractSuccess(r), r);
                using var doc = JsonDocument.Parse(r);
                Assert.False(doc.RootElement.GetProperty("data").TryGetProperty("tag", out _),
                    $"empty tag echo must be omitted, got: {r}");
            }
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task NamedTagResponses_echo_the_tag()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var results = new[]
            {
                await tools.OpenPort("COM10", tag: "a"),
                await tools.Send("Hi", tag: "a"),
                await tools.WaitForQuiet(quietMs: 50, timeoutMs: 2000, tag: "a"),
                await tools.ReadData(tag: "a"),
                await tools.WaitForResponse("NEVER-MATCHES", timeoutMs: 200, tag: "a"),
                await tools.ClearBuffer(tag: "a")
            };

            foreach (var r in results)
            {
                Assert.True(ToolContextFactory.ExtractSuccess(r), r);
                using var doc = JsonDocument.Parse(r);
                Assert.Equal("a", doc.RootElement.GetProperty("data").GetProperty("tag").GetString());
            }
        }
        finally { sp.Dispose(); }
    }

    // ── R1 robustness contracts: guard envelope, tag honesty, pattern validation ──

    [Fact]
    public async Task Guard_ConvertsExceptionIntoInternalEnvelope()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var result = await ctx.Guard(() => throw new InvalidOperationException("boom"));
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INTERNAL", ToolContextFactory.ExtractErrorCode(result));
            Assert.Contains("boom", ToolContextFactory.ExtractError(result) ?? "");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ListOpenPorts_WhenSerialThrows_ReturnsInternalEnvelope()
    {
        // Any exception escaping a tool body must come back as the stable
        // {"success":false,"error":{code,message}} envelope, never raw text.
        var ctx = new ToolContext(new MultiPortService(() => new VirtualSerialService()), new ThrowingSerialService());
        var tools = new SerialTools(ctx);

        var result = await tools.ListOpenPorts();
        Assert.False(ToolContextFactory.ExtractSuccess(result));
        Assert.Equal("INTERNAL", ToolContextFactory.ExtractErrorCode(result));
        Assert.Contains("boom", ToolContextFactory.ExtractError(result) ?? "");
    }

    [Fact]
    public async Task ReadData_UnknownTag_ReportsPortNotOpen_WithoutAllocatingBuffer()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ReadData(tag: "typo");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
            Assert.Empty(ctx.Buffers); // no orphan buffer for a typo'd tag
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClearBuffer_UnknownTag_ReportsPortNotOpen_WithoutAllocatingBuffer()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClearBuffer(tag: "typo");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
            Assert.Empty(ctx.Buffers);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_UnknownTag_ReportsPortNotOpen_Immediately()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await tools.WaitForResponse("OK", timeoutMs: 5000, tag: "typo");
            sw.Stop();
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
            // Must fail fast, i.e. long before the 5000ms wait timeout would
            // elapse (the old behavior blocked the whole timeout on a typo'd tag).
            Assert.True(sw.ElapsedMilliseconds < 5000, $"unknown tag must fail fast, took {sw.ElapsedMilliseconds}ms");
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForQuiet_UnknownTag_ReportsPortNotOpen()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForQuiet(quietMs: 50, timeoutMs: 300, tag: "typo");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ClosePort_UnknownTag_ReportsPortNotOpen()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.ClosePort(tag: "typo");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("PORT_NOT_OPEN", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_UnknownMatchMode_ReportsInvalidPattern()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForResponse("OK", timeoutMs: 200, matchMode: "startswith");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INVALID_PATTERN", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_InvalidRegex_ReportsInvalidPattern()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForResponse("[unclosed", timeoutMs: 200, matchMode: "regex");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INVALID_PATTERN", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_InvalidRegex_ReportsInvalidPattern_AndDoesNotSend()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");
            var result = await tools.SendAndWait("DATA", "([bad", matchMode: "regex");
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INVALID_PATTERN", ToolContextFactory.ExtractErrorCode(result));
            var service = (VirtualSerialService)ctx.Serial;
            Assert.Empty(service.GetSentData());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task WaitForResponse_MixedCaseMode_IsAccepted()
    {
        // Mode names are matched OrdinalIgnoreCase by the matcher — the
        // validator must accept the same set, not just lowercase literals.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.WaitForResponse("OK", timeoutMs: 150, matchMode: "EXACT");
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task OpenPort_AlreadyOpen_DefaultSession_ReportsFullShape()
    {
        // already-open must parse with the same schema as a fresh open
        // (port/baudRate/dataBits), not a bare {message,port} — and it reports
        // the config actually applied at open time, not this call's arguments.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            Assert.True(ToolContextFactory.ExtractSuccess(await tools.OpenPort("COM10", 9600, 7)));
            var result = await tools.OpenPort("COM12", 115200); // different args, same session
            Assert.True(ToolContextFactory.ExtractSuccess(result));
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("Port already open", data.GetProperty("message").GetString());
            Assert.Equal("COM10", data.GetProperty("port").GetString());
            Assert.Equal(9600, data.GetProperty("baudRate").GetInt32());
            Assert.Equal(7, data.GetProperty("dataBits").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task SendAndWait_Timeout_ReportsByteLength()
    {
        // send_and_wait performs the same send as send — its response must
        // carry the same byteLength contract (UTF-8 count for text mode).
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            await tools.OpenPort("COM10");
            var result = await tools.SendAndWait("HELLO", "NEVER-MATCHES", timeoutMs: 150);
            using var doc = JsonDocument.Parse(result);
            var data = doc.RootElement.GetProperty("data");
            Assert.False(data.GetProperty("matched").GetBoolean());
            Assert.Equal(5, data.GetProperty("byteLength").GetInt32());
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_DefaultCap_TruncatesHugeEntries()
    {
        // Omitted maxLength defaults to a 2000-char server cap so a binary
        // flood cannot dump tens of MB into the model context.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = new string('A', 5000) });

            var result = await tools.ReadData();
            using var doc = JsonDocument.Parse(result);
            var text = doc.RootElement.GetProperty("data").GetProperty("entries")[0].GetProperty("text").GetString();
            Assert.NotNull(text);
            Assert.True(text!.Length < 5000, $"default cap must truncate, got {text.Length} chars");
            Assert.Equal(2001, text.Length); // cap chars + ellipsis marker
            Assert.EndsWith("…", text);
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task ReadData_ExplicitMaxLength_ClampedToCeiling()
    {
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            ctx.Buffer.AddEntry(new LogEntry { Id = 1, Direction = "RX", Text = new string('B', 70_000) });

            var result = await tools.ReadData(maxLength: 100_000);
            using var doc = JsonDocument.Parse(result);
            var text = doc.RootElement.GetProperty("data").GetProperty("entries")[0].GetProperty("text").GetString();
            Assert.NotNull(text);
            Assert.Equal(65_537, text!.Length); // 65536 cap + ellipsis marker
            Assert.EndsWith("…", text);
        }
        finally { sp.Dispose(); }
    }

    [Theory]
    [InlineData(0, 8, 1, 0)]      // baudRate <= 0
    [InlineData(115200, 4, 1, 0)] // dataBits < 5
    [InlineData(115200, 9, 1, 0)] // dataBits > 8
    [InlineData(115200, 8, 3, 0)] // stopBits > 2
    [InlineData(115200, 8, 1, 5)] // parity > 2
    public async Task OpenPort_OutOfRangeConfig_ReportsInvalidConfig(int baudRate, int dataBits, int stopBits, int parity)
    {
        // Out-of-range numeric config must fail with a precise INVALID_CONFIG
        // instead of an ArgumentException inside SerialPort surfacing as a
        // generic OPEN_FAILED.
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.OpenPort("COM10", baudRate, dataBits, stopBits, parity);
            Assert.False(ToolContextFactory.ExtractSuccess(result));
            Assert.Equal("INVALID_CONFIG", ToolContextFactory.ExtractErrorCode(result));
        }
        finally { sp.Dispose(); }
    }

    [Fact]
    public async Task OpenPort_InRangeConfig_StillOpens()
    {
        // Guard the validator against overreach: the documented 5-8/0-2/0-2
        // ranges must keep opening normally (7E2 is a real-world combo).
        var (ctx, sp) = ToolContextFactory.Create();
        try
        {
            var tools = new SerialTools(ctx);
            var result = await tools.OpenPort("COM10", 9600, 7, 2, 2);
            Assert.True(ToolContextFactory.ExtractSuccess(result));
        }
        finally { sp.Dispose(); }
    }

    /// <summary>ISerialService fake whose every member throws — proves the
    /// Guard wrapper converts dependency explosions into the error envelope.</summary>
    private sealed class ThrowingSerialService : ISerialService
    {
        public bool IsOpen => throw new InvalidOperationException("boom");
        public string? CurrentPort => throw new InvalidOperationException("boom");
        public int BaudRate => throw new InvalidOperationException("boom");
        public SerialConfig? ActiveConfig => throw new InvalidOperationException("boom");
#pragma warning disable CS0067 // events are never raised by this fake
        public event Action<LogEntry>? OnDataReceived;
        public event Action<string>? OnError;
        public event Action? OnDisconnected;
        public event Action<string>? OnDeviceWait;
#pragma warning restore CS0067
        public bool Open(SerialConfig config) => throw new InvalidOperationException("boom");
        public bool Send(string data, bool isHex = false) => throw new InvalidOperationException("boom");
        public bool SendHex(string hex) => throw new InvalidOperationException("boom");
        public bool Close() => throw new InvalidOperationException("boom");
        public void Dispose() { }
    }
}
