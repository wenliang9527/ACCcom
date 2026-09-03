using System.Text.Json;
using EmbedIO;
using EmbedIO.WebSockets;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SerialWebSocketHandler : WebSocketModule, IDisposable
{
    private readonly HttpService _service;
    private bool _disposed;
    private int _activeClients;
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
        // Skip serialization entirely when nobody is connected. The dashboard
        // is the only WebSocket consumer, so without clients this was paying a
        // full JSON serialize + BroadcastAsync per received frame.
        if (Volatile.Read(ref _activeClients) == 0) return;
        HandleDataEntry(entry);
    }

    internal void HandleDataEntry(LogEntry entry)
    {
        if (_activeClients > 0)
            _ = BroadcastAsync(JsonSerializer.Serialize(entry, _wsJsonOptions));
    }

    internal int ActiveClientCount => Volatile.Read(ref _activeClients);

    // Test-only hooks: EmbedIO invokes the protected overrides in production;
    // these let unit tests drive the same counting logic without a live server.
    internal void SimulateClientConnected() => OnClientConnectedAsync(null!);
    internal void SimulateClientDisconnected() => OnClientDisconnectedAsync(null!);

    protected override Task OnMessageReceivedAsync(IWebSocketContext context, byte[] buffer, IWebSocketReceiveResult result)
    {
        return Task.CompletedTask;
    }

    protected override Task OnClientConnectedAsync(IWebSocketContext context)
    {
        Interlocked.Increment(ref _activeClients);
        return Task.CompletedTask;
    }

    protected override Task OnClientDisconnectedAsync(IWebSocketContext context)
    {
        Interlocked.Decrement(ref _activeClients);
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
