using System.Text.Json;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// End-to-end RX path without the WPF ViewModel: virtual serial injects bytes,
/// a FrameBuffer assembles frames, and assembled frames land in the HttpService
/// buffer where an HTTP poll sees them. This mirrors DataFlowViewModel's wiring
/// (FrameBuffer.OnFrameAssembled -> OnFrameReady -> http.AddEntry) and guards the
/// merged single-frame-path contract introduced when the legacy assembler was
/// retired.
/// </summary>
[Collection("SerialTcp")]
public class RxPipelineIntegrationTests : IDisposable
{
    private readonly VirtualSerialService _serial;
    private readonly HttpService _http;
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public RxPipelineIntegrationTests()
    {
        _serial = new VirtualSerialService();
        _serial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200 });
        _baseUrl = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        _http = new HttpService(new HttpServiceOptions { SerialService = _serial, Url = _baseUrl });
        _http.Start();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
        _serial.Dispose();
    }

    private FrameBuffer CreateBuffer(FrameBufferConfig config)
    {
        var buffer = new FrameBuffer(config);
        // Same wiring as DataFlowViewModel.OnFrameReady -> http.AddEntry.
        buffer.OnFrameAssembled += entry => _http.AddEntry(entry);
        return buffer;
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, int since = 0)
    {
        var resp = await client.GetAsync($"/api/data?since={since}");
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task VirtualSerial_FrameBuffer_HttpPoll_SeesAssembledFrame()
    {
        using var buffer = CreateBuffer(new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = [0xAA, 0x55],
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            LengthFieldIncludes = 0,
            BufferCapacity = 4096,
            MaxFrameSize = 4096
        });

        // Serial RX -> buffer (the ViewModel would call this in OnSerialData).
        _serial.OnDataReceived += entry =>
        {
            if (entry.Direction == "RX")
            {
                var bytes = HexHelper.HexStringToBytes(entry.RawHex);
                buffer.Write(bytes);
            }
        };

        // Inject a frame split across two packets: the length byte equals the
        // whole frame size (header + length + payload), 6 = [AA 55 06 01 02 03].
        _serial.InjectRxData("AA 55 06 01 02");
        _serial.InjectRxData("03");

        // Assembled frame surfaces via OnFrameAssembled -> http.AddEntry; poll it.
        var data = await GetDataAsync(_client);
        Assert.True(data.GetProperty("Success").GetBoolean());
        var entries = data.GetProperty("Data").GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("AA5506010203", entries[0].GetProperty("RawHex").GetString()!.Replace(" ", ""));
        Assert.Equal("RX", entries[0].GetProperty("Direction").GetString());
    }

    [Fact]
    public async Task FrameBuffer_WithLengthField_EmitsExactlyOnce()
    {
        using var buffer = CreateBuffer(new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByLengthField,
            LengthFieldOffset = 0,
            LengthFieldSize = 1,
            LengthFieldIncludes = 0,
            BufferCapacity = 4096,
            MaxFrameSize = 4096
        });

        _serial.OnDataReceived += entry =>
        {
            if (entry.Direction == "RX")
            {
                var bytes = HexHelper.HexStringToBytes(entry.RawHex);
                buffer.Write(bytes);
            }
        };

        // One 3-byte frame [03 AA BB] + trailing garbage [CC] that must not
        // form a second frame (length field says 3).
        _serial.InjectRxData("03 AA BB CC");

        var data = await GetDataAsync(_client);
        var entries = data.GetProperty("Data").GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("03AABB", entries[0].GetProperty("RawHex").GetString()!.Replace(" ", ""));
    }

    [Fact]
    public async Task NoFrameAssembler_RawBytesLandInBuffer()
    {
        // Without frame assembly, the ViewModel routes RX straight to
        // http.AddEntry (the direct display path). Mirror that here.
        _serial.OnDataReceived += entry => _http.AddEntry(entry);
        _serial.InjectRxData("DE AD BE EF");

        var data = await GetDataAsync(_client);
        var entries = data.GetProperty("Data").GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        Assert.Equal("DEADBEEF", entries[0].GetProperty("RawHex").GetString()!.Replace(" ", ""));
    }
}
