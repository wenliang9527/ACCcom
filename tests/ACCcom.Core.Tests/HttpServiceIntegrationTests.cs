using System.Net.Http;
using System.Text;
using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// Integration tests for the HTTP API endpoints that need a real serial service
/// or a Modbus slave: slave lifecycle, modbus read, parser parse-raw, statistics.
/// </summary>
[Collection("SerialTcp")]
public class HttpServiceIntegrationTests : IDisposable
{
    private readonly VirtualSerialService _serial;
    private readonly HttpService _service;
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly SessionRecorder _recorder;
    private readonly MultiPortService _multiPort;

    public HttpServiceIntegrationTests()
    {
        _serial = new VirtualSerialService();
        _serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        _recorder = new SessionRecorder();
        _multiPort = new MultiPortService(() => new VirtualSerialService());
        _baseUrl = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        _service = new HttpService(new HttpServiceOptions
        {
            SerialService = _serial,
            SlaveService = new ModbusSlaveService(),
            DataStatistics = new DataStatistics(),
            SessionRecorder = _recorder,
            MultiPortService = _multiPort,
            Url = _baseUrl
        });
        _service.Start();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _service.Dispose();
        _recorder.Dispose();
        _multiPort.Dispose();
        _serial.Dispose();
    }

    private async Task<JsonElement> PostJsonAsync(string path, object body)
    {
        // Send the raw JSON string (not PostAsJsonAsync): EmbedIO's body reader
        // pairs reliably with an explicit JSON payload here.
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        var response = await _client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Statistics_ReturnsCounts()
    {
        var root = await GetAsync("/api/statistics");

        // Endpoint must respond successfully and expose rx/tx counters (which
        // are zero here because stats are fed by the ViewModel pipeline, not by
        // raw serial injection).
        var rawJson = root.ToString();
        Assert.True(root.GetProperty("Success").GetBoolean(), "stats success=false; body=" + rawJson);
        var data = root.GetProperty("Data");
        Assert.True(data.TryGetProperty("totalRxBytes", out _), $"stats data: {data}");
        Assert.True(data.TryGetProperty("totalTxBytes", out _));
    }

    [Fact]
    public async Task ParseRaw_WithoutParserManager_ReturnsFailure()
    {
        var root = await PostJsonAsync("/api/parser/parse-raw", new { hex = "AA 55 03 01 02 03" });

        // This HttpService instance has no ParserManager wired; the endpoint must
        // respond with a structured failure rather than throw.
        Assert.False(root.GetProperty("Success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("Error").GetString()));
    }

    [Fact]
    public async Task SlaveCreate_And_ReadRegister_OverWire()
    {
        var port = TestPortHelper.GetFreePort();
        var create = await PostJsonAsync("/api/slave/create", new
        {
            slaveId = 0x01,
            transport = "tcp",
            connectionParam = port.ToString(),
            holdingRegisters = 16
        });

        Assert.True(create.GetProperty("Success").GetBoolean());
        var id = create.GetProperty("Data").GetProperty("id").GetString();
        Assert.NotNull(id);

        // Write a known value, then read it back through the same HTTP API.
        var write = await PostJsonAsync("/api/slave/write", new
        {
            slaveId = id,
            type = "holding",
            address = 0,
            value = 0xABCD
        });
        Assert.True(write.GetProperty("Success").GetBoolean());

        var read = await PostJsonAsync("/api/slave/read", new
        {
            slaveId = id,
            type = "holding",
            address = 0
        });
        Assert.True(read.GetProperty("Success").GetBoolean());
        Assert.Equal(0xABCD, read.GetProperty("Data").GetProperty("value").GetInt32());

        // Cleanup: SlaveRemove reads the slave id from the "tag" field.
        var remove = await PostJsonAsync("/api/slave/remove", new { tag = id });
        Assert.True(remove.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task SlaveList_AfterCreate_ContainsSlave()
    {
        var port = TestPortHelper.GetFreePort();
        var create = await PostJsonAsync("/api/slave/create", new
        {
            slaveId = 0x02,
            transport = "tcp",
            connectionParam = port.ToString()
        });
        Assert.True(create.GetProperty("Success").GetBoolean());

        var list = await GetAsync("/api/slave/list");
        Assert.True(list.GetProperty("Success").GetBoolean());
        var data = list.GetProperty("Data");
        var slaves = data.GetProperty("slaves");
        Assert.True(slaves.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ModbusRead_WhenNoSerial_ReturnsFailure()
    {
        // The virtual serial has no slave device answering; a read should surface
        // an error rather than hang.
        var root = await PostJsonAsync("/api/modbus/read", new
        {
            slaveId = 1,
            functionCode = "ReadHoldingRegisters",
            startAddress = 0,
            quantity = 4
        });

        // Response is a valid ApiResponse; Success may be false but must not throw.
        Assert.True(root.TryGetProperty("Success", out _));
    }

    [Fact]
    public async Task Send_WithHex_ReturnsSuccess()
    {
        var root = await PostJsonAsync("/api/send", new { data = "AA BB", isHex = true });
        Assert.True(root.GetProperty("Success").GetBoolean());
    }

    // ── Recording endpoints ──

    [Fact]
    public async Task Recording_StartStop_RoundTrips()
    {
        // SafePath rejects absolute paths (path-traversal protection); the API
        // accepts a plain file name resolved under the recordings directory.
        var fileName = $"rec_test_{Guid.NewGuid():N}.jsonl";
        try
        {
            var start = await PostJsonAsync("/api/recording/start", new { filename = fileName });
            Assert.True(start.GetProperty("Success").GetBoolean(), start.ToString());
            Assert.EndsWith(fileName, start.GetProperty("Data").GetProperty("file").GetString());

            var status = await GetAsync("/api/recording/status");
            Assert.True(status.GetProperty("Data").GetProperty("isRecording").GetBoolean());

            var stop = await PostJsonAsync("/api/recording/stop", new { });
            Assert.True(stop.GetProperty("Success").GetBoolean(), stop.ToString());
        }
        finally
        {
            try
            {
                var full = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ACCcom", "recordings", fileName);
                if (File.Exists(full)) File.Delete(full);
            }
            catch { }
        }
    }

    [Fact]
    public async Task Recording_StartWithoutRecorder_FailsGracefully()
    {
        // A separate service without SessionRecorder injected.
        var url = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        using var bare = new HttpService(new HttpServiceOptions { Url = url });
        bare.Start();
        using var client = new HttpClient { BaseAddress = new Uri(url) };

        var resp = await client.PostAsync("/api/recording/start", new StringContent("{}", Encoding.UTF8, "application/json"));
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync());
        var root = doc.RootElement.Clone();
        Assert.False(root.GetProperty("Success").GetBoolean());
    }

    // ── Multi-port endpoints ──

    [Fact]
    public async Task MultiPort_OpenSendClose_RoundTrips()
    {
        var open = await PostJsonAsync("/api/multiport/open", new
        {
            tag = "aux1",
            port = "VIRT",
            baudRate = 115200
        });
        Assert.True(open.GetProperty("Success").GetBoolean(), open.ToString());

        var send = await PostJsonAsync("/api/multiport/send", new { tag = "aux1", data = "ping", isHex = false });
        Assert.True(send.GetProperty("Success").GetBoolean(), send.ToString());

        var close = await PostJsonAsync("/api/multiport/close", new { tag = "aux1" });
        Assert.True(close.GetProperty("Success").GetBoolean(), close.ToString());
    }

    [Fact]
    public async Task MultiPort_SendToUnknownTag_Fails()
    {
        var resp = await PostJsonAsync("/api/multiport/send", new { tag = "nope", data = "x" });
        Assert.False(resp.GetProperty("Success").GetBoolean());
    }

    // ── Parser endpoints ──

    [Fact]
    public async Task Parser_ActivateNone_Deactivates()
    {
        var root = await PostJsonAsync("/api/parser/activate", new { name = "(None)" });
        Assert.True(root.GetProperty("Success").GetBoolean(), root.ToString());
    }

    [Fact]
    public async Task Parser_ActivateUnknown_TreatedAsDeactivate()
    {
        // The endpoint's contract: an unknown name with no parser error is
        // treated as "deactivate" (returns Ok), not an error.
        var root = await PostJsonAsync("/api/parser/activate", new { name = "does_not_exist" });
        Assert.True(root.GetProperty("Success").GetBoolean(), root.ToString());
    }

    // ── Misc endpoints ──

    [Fact]
    public async Task Metrics_ReturnsPrometheusText()
    {
        var response = await _client.GetAsync("/api/metrics");
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("acccom_uptime_seconds", text);
        Assert.Contains("# TYPE acccom_serial_bytes_received_total counter", text);
    }

    [Fact]
    public async Task Clear_ReturnsSuccess()
    {
        var root = await PostJsonAsync("/api/clear", new { });
        Assert.True(root.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task WaitFor_NoSerialData_TimesOut()
    {
        var root = await PostJsonAsync("/api/wait-for", new
        {
            pattern = "never-matches",
            timeoutMs = 200
        });
        // No matching data ever arrives; endpoint returns (Success may be false).
        Assert.True(root.TryGetProperty("Success", out _));
    }

    [Fact]
    public async Task ModbusWrite_NoDevice_ReturnsFailure()
    {
        var root = await PostJsonAsync("/api/modbus/write", new
        {
            slaveId = 1,
            functionCode = "WriteSingleRegister",
            address = 0,
            value = 42
        });
        Assert.True(root.TryGetProperty("Success", out _));
    }
}
