using System.Text.Json;
using EmbedIO;
using EmbedIO.WebSockets;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialWebSocketHandler : WebSocketModule, IDisposable
{
    private readonly HttpService _service;
    private bool _disposed;
    private static readonly JsonSerializerOptions _wsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SerialWebSocketHandler(string urlPath, HttpService service) : base(urlPath, true)
    {
        _service = service;
        _service.OnDataEntry += OnDataEntry;
    }

    private void OnDataEntry(LogEntry entry)
    {
        _ = BroadcastAsync(JsonSerializer.Serialize(entry, _wsJsonOptions));
    }

    protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
    {
        return Task.CompletedTask;
    }

    protected override Task OnClientConnectedAsync(IWebSocketContext context)
    {
        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        return Task.CompletedTask;
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _service.OnDataEntry -= OnDataEntry;
        base.Dispose();
    }
}
