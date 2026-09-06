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

    [Fact]
    public async Task ClosePort_WhenNotOpen_ReturnsSuccess()
    {
        // Act
        var root = await PostEmptyAsync("/api/port/close");

        // Assert
        Assert.True(root.GetProperty("Success").GetBoolean());
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
}
