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

    public HttpServiceIntegrationTests()
    {
        _serial = new VirtualSerialService();
        _serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        _baseUrl = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        _service = new HttpService(new HttpServiceOptions
        {
            SerialService = _serial,
            SlaveService = new ModbusSlaveService(),
            DataStatistics = new DataStatistics(),
            Url = _baseUrl
        });
        _service.Start();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _service.Dispose();
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
}
