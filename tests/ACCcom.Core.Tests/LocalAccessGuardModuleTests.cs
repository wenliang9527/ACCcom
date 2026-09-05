using System.Net;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>End-to-end tests for the local HTTP API security filter: token
/// enforcement on /api and /ws, and loopback-only Host enforcement.</summary>
public class LocalAccessGuardModuleTests : IDisposable
{
    private const string BaseUrl = "http://127.0.0.1:18977";
    private const string Token = "s3cret-token";
    private readonly HttpService _service;
    private readonly HttpClient _client;

    public LocalAccessGuardModuleTests()
    {
        _service = new HttpService(new HttpServiceOptions
        {
            Url = BaseUrl,
            ApiToken = Token
        });
        _service.Start();
        _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public void Dispose()
    {
        _client.Dispose();
        _service.Dispose();
    }

    [Fact]
    public async Task ApiRoute_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WithTokenHeader_Returns200()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        req.Headers.Add("X-ACCcom-Token", Token);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WithTokenQueryParam_Returns200()
    {
        var response = await _client.GetAsync("/api/status?token=" + Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_WrongToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        req.Headers.Add("X-ACCcom-Token", "wrong");
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRoute_MissingTokenOnPost_Returns401()
    {
        var response = await _client.PostAsync("/api/send", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
