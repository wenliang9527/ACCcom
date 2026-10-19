using System.Text.Json;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>
/// /api/parser/parse-raw used to be the only POST endpoint running bare:
/// <see cref="HttpService.ParseRawHexAsync"/> reads the parser file after a
/// File.Exists check (a TOCTOU window), and any escape surfaced as an
/// unhandled EmbedIO 500 instead of the ApiResponse.Fail every sibling
/// endpoint returns. Locking the file reproduces the IOException
/// deterministically.
/// </summary>
[Collection("SerialTcp")]
public class SerialControllerParseRawTests : IDisposable
{
    private readonly HttpService _service;
    private readonly HttpClient _client;
    private readonly string _baseUrl;
    private readonly string _parserDir;

    public SerialControllerParseRawTests()
    {
        _parserDir = Path.Combine(Path.GetTempPath(), "acccom_parseraw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_parserDir);
        _baseUrl = $"http://127.0.0.1:{TestPortHelper.GetFreePort()}";
        _service = new HttpService(new HttpServiceOptions
        {
            ParserManager = new ParserManager(_parserDir),
            Url = _baseUrl
        });
        _service.Start();
        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _service.Dispose();
        try { Directory.Delete(_parserDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ParseRaw_UnreadableParserFile_ReturnsCleanFailureNotServerError()
    {
        var path = Path.Combine(_parserDir, "locked.csx");
        await File.WriteAllTextAsync(path, "return new List<FieldAnnotation>();");
        // FileShare.None makes File.ReadAllText inside ParseRawHexAsync throw
        // IOException — the TOCTOU window the endpoint used to leave unguarded.
        using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var payload = JsonSerializer.Serialize(new { hex = "01 03 00 00 00 01", parserName = "locked" });
        var response = await _client.PostAsync("/api/parser/parse-raw",
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        // A 200 carrying Success=false — not a 500, and not an exception.
        response.EnsureSuccessStatusCode();
        var root = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement.Clone();
        Assert.False(root.GetProperty("Success").GetBoolean());
        Assert.Contains("解析数据异常", root.GetProperty("Error").GetString());
    }
}
