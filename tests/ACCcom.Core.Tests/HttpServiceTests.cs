using System.Text.Json;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

[Collection("SerialTcp")]
public class HttpServiceTests : IDisposable
{
    private readonly HttpService _service;
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public HttpServiceTests()
    {
        _baseUrl = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        _service = new HttpService(new HttpServiceOptions { Url = _baseUrl });
        _service.Start();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _service.Dispose();
    }

    // ApiResponse serializes with PascalCase (Success, Error, Data).
    // Anonymous objects inside Data serialize with camelCase.
    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> PostEmptyAsync(string path)
    {
        var response = await _client.PostAsync(path, null);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Constructor_WithSerialServiceAndParserManager_CreatesInstance()
    {
        // Arrange & Act
        using var service = new HttpService(new HttpServiceOptions
        {
            SerialService = new SerialService(),
            ParserManager = new ParserManager(),
            Url = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}"
        });

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithDefaults_CreatesInstance()
    {
        // Arrange & Act
        using var service = new HttpService(new HttpServiceOptions());

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public async Task Health_ReturnsOkWithStatus()
    {
        // Act
        var root = await GetAsync("/api/health");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
        var data = root.GetProperty("Data");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.True(data.TryGetProperty("time", out _));
    }

    [Fact]
    public async Task Status_ReturnsPortInfo()
    {
        // Act
        var root = await GetAsync("/api/status");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
        var data = root.GetProperty("Data");
        Assert.False(data.GetProperty("isOpen").GetBoolean());
        Assert.Equal(0, data.GetProperty("baudRate").GetInt32());
        Assert.Equal(0, data.GetProperty("rxCount").GetInt32());
        Assert.Equal(0, data.GetProperty("txCount").GetInt32());
    }

    [Fact]
    public async Task Ports_ReturnsAvailablePortsList()
    {
        // Act
        var root = await GetAsync("/api/ports");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
        var data = root.GetProperty("Data");
        Assert.True(data.TryGetProperty("ports", out var ports));
        Assert.Equal(JsonValueKind.Array, ports.ValueKind);
    }

    [Fact]
    public async Task Data_ReturnsEmptyEntries_WhenNoData()
    {
        // Act
        var root = await GetAsync("/api/data");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
        var data = root.GetProperty("Data");
        Assert.Equal(0, data.GetProperty("count").GetInt32());
        Assert.Equal(0, data.GetProperty("latestId").GetInt32());
    }

    [Fact]
    public async Task Send_WithEmptyBody_ReturnsFailure()
    {
        // Arrange
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/send", content);

        // Assert
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.False(root.GetProperty("Success").GetBoolean());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("Application/JSON")]
    [InlineData("APPLICATION/JSON")]
    public async Task Send_JsonContentType_IsCaseInsensitive(string contentType)
    {
        // RFC 9110: media types are case-insensitive. A regression that only
        // matches exact "application/json" would fall into the raw-text branch
        // and try to send the JSON body as serial data.
        var content = new StringContent("""{"data":"AA BB","isHex":true}""",
            System.Text.Encoding.UTF8, contentType);

        var response = await _client.PostAsync("/api/send", content);

        // No serial port open → structured failure (not a raw-text parse path
        // that would also fail — but Success/Error shape must remain ApiResponse).
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(root.TryGetProperty("Success", out _));
        Assert.True(root.TryGetProperty("Error", out _));
    }

    [Fact]
    public async Task Send_PlainTextBody_IsRawTextPath()
    {
        var content = new StringContent("AT+GMR", System.Text.Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/api/send", content);

        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.False(root.GetProperty("Success").GetBoolean()); // port not open
        Assert.Contains("发送", root.GetProperty("Error").GetString() ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_HexPrefixPlainText_IsHexPath()
    {
        var content = new StringContent("hex:AA BB", System.Text.Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/api/send", content);

        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.False(root.GetProperty("Success").GetBoolean());
    }

    // ── SerialController endpoint gaps: clear / data limit / wait-for clamp ──

    [Fact]
    public async Task Clear_PlainTextBody_IsAccepted()
    {
        // Fallback path: no JSON content-type, raw body "rx" → directional clear.
        var content = new StringContent("rx", System.Text.Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/api/clear", content);
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(root.GetProperty("Success").GetBoolean());
        Assert.Equal("rx", root.GetProperty("Data").GetProperty("cleared").GetString());
    }

    // ── ClearRequested: API → UI sync contract ──

    [Fact]
    public void ClearBuffer_Raises_OnClearRequested_WithTarget()
    {
        // The GUI subscribes to mirror /api/clear into its display lists; the
        // event must carry the same target the buffer was cleared with.
        _service.AddEntry(new ACCcom.Core.Models.LogEntry
        {
            Id = 1,
            Direction = "RX",
            Text = "to-clear",
            RawHex = "AA",
            Timestamp = DateTime.Now
        });
        string? seen = "unset";
        _service.OnClearRequested += t => seen = t;

        _service.ClearBuffer("rx");

        Assert.Equal("rx", seen);
        Assert.Empty(_service.GetEntriesSince(0)); // buffer really was cleared
    }

    [Fact]
    public void ClearBuffer_WithRaiseEventFalse_DoesNotRaise()
    {
        // UI-initiated clears pass raiseEvent:false — the UI already cleared
        // its own lists and must not get an echo back.
        var raised = 0;
        _service.OnClearRequested += _ => Interlocked.Increment(ref raised);

        _service.ClearBuffer(null, raiseEvent: false);

        Assert.Equal(0, raised);
        Assert.Empty(_service.GetEntriesSince(0));
    }

    [Fact]
    public async Task Clear_Api_Endpoint_Raises_OnClearRequested()
    {
        // End-to-end: POST /api/clear must surface as an event so an attached
        // GUI stays in lockstep with the API plane.
        var raised = 0;
        string? target = null;
        _service.OnClearRequested += t => { target = t; Interlocked.Increment(ref raised); };

        var content = new StringContent("rx", System.Text.Encoding.UTF8, "text/plain");
        var response = await _client.PostAsync("/api/clear", content);
        response.EnsureSuccessStatusCode();

        Assert.Equal(1, raised);
        Assert.Equal("rx", target);
    }

    [Fact]
    public async Task Data_Limit_ReturnsAtMostThatManyEntries()
    {
        for (int i = 1; i <= 5; i++)
            _service.AddEntry(new ACCcom.Core.Models.LogEntry
            {
                Id = i,
                Direction = "RX",
                Text = $"e{i}",
                RawHex = $"{i:X2}",
                Timestamp = DateTime.Now
            });

        var root = await GetAsync("/api/data?since=0&limit=2");
        Assert.True(root.GetProperty("Success").GetBoolean());
        Assert.Equal(2, root.GetProperty("Data").GetProperty("count").GetInt32());
        Assert.Equal(2, root.GetProperty("Data").GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task WaitFor_TimeoutBelowClampFloor_StillSucceedsWithClampedWait()
    {
        // SerialController clamps timeoutMs into [100, 60000]. timeoutMs=1 must not
        // throw or reject; it waits at least the 100ms floor and returns matched=false.
        var content = new StringContent(
            """{"pattern":"never-seen","timeoutMs":1}""",
            System.Text.Encoding.UTF8, "application/json");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _client.PostAsync("/api/wait-for", content);
        sw.Stop();
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.True(root.GetProperty("Success").GetBoolean());
        Assert.False(root.GetProperty("Data").GetProperty("matched").GetBoolean());
        Assert.True(sw.ElapsedMilliseconds >= 90,
            $"expected clamp floor ~100ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ClosePort_WhenNotOpen_ReturnsSuccess()
    {
        // Act
        var root = await PostEmptyAsync("/api/port/close");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public void OpenPort_null_or_empty_port_returns_false()
    {
        // A null/invalid request must not NRE on req.Port — it fails cleanly.
        Assert.False(_service.OpenPort(null));
        Assert.False(_service.OpenPort(new ACCcom.Core.Models.OpenPortRequest { Port = "" }));
    }

    [Fact]
    public void ReadParserCode_without_parser_manager_returns_null()
    {
        // The default test service has no ParserManager injected; the read
        // path must report "unavailable" as null rather than NRE.
        Assert.Null(_service.ReadParserCode("any"));
        Assert.Null(_service.ReadParserCode(null!));
    }

    [Fact]
    public void WriteParserCode_without_parser_manager_returns_failure()
    {
        var (ok, error) = _service.WriteParserCode("any", "code");

        Assert.False(ok);
        Assert.Equal("ParserManager not available", error);
    }

    [Fact]
    public async Task Parsers_ReturnsParserList()
    {
        // Act
        var root = await GetAsync("/api/parsers");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
        var data = root.GetProperty("Data");
        Assert.True(data.TryGetProperty("parsers", out var parsers));
        Assert.Equal(JsonValueKind.Array, parsers.ValueKind);
    }

    [Fact]
    public void AddEntry_null_is_noop_and_does_not_raise_event()
    {
        var raised = 0;
        _service.OnDataEntry += _ => Interlocked.Increment(ref raised);

        _service.AddEntry(null);

        Assert.Equal(0, raised);
        Assert.Empty(_service.GetEntriesSince(0));
    }

    [Fact]
    public void AddEntry_valid_entry_raises_event_and_buffers()
    {
        var raised = 0;
        _service.OnDataEntry += _ => Interlocked.Increment(ref raised);
        var entry = new ACCcom.Core.Models.LogEntry { Id = 1, Timestamp = DateTime.UtcNow, Direction = "RX", Text = "hello" };

        _service.AddEntry(entry);

        Assert.Equal(1, raised);
        var entries = _service.GetEntriesSince(0);
        Assert.Single(entries);
        Assert.Equal("hello", entries[0].Text);
    }

    [Fact]
    public void MultiPortOpen_NullRequest_ReturnsFalse()
    {
        Assert.False(_service.MultiPortOpen(null));
    }

    [Fact]
    public void MultiPortSend_NullArgs_ReturnsFalse()
    {
        Assert.False(_service.MultiPortSend(null, "data", false));
        Assert.False(_service.MultiPortSend("tag", null, false));
    }

    [Fact]
    public void SlaveCreate_NullRequest_ReturnsNull()
    {
        Assert.Null(_service.SlaveCreate(null));
    }

    [Fact]
    public void SlaveRemove_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(_service.SlaveRemove(null));
        Assert.False(_service.SlaveRemove(""));
    }

    [Fact]
    public void RecordingStart_TraversalName_Rejected()
    {
        var (ok, _, error) = _service.RecordingStart("../evil.jsonl");
        Assert.False(ok);
        Assert.NotNull(error);
    }
}
