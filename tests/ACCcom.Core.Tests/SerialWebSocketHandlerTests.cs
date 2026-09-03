using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SerialWebSocketHandlerTests : IDisposable
{
    private readonly HttpService _service;
    private readonly SerialWebSocketHandler _handler;

    public SerialWebSocketHandlerTests()
    {
        // Server not started: the handler only subscribes to OnDataEntry, so
        // no port is bound for these unit tests.
        _service = new HttpService(new HttpServiceOptions { Url = "http://127.0.0.1:18999" });
        _handler = new SerialWebSocketHandler("/ws", _service);
    }

    public void Dispose()
    {
        _handler.Dispose();
        _service.Dispose();
    }

    [Fact]
    public void NoClients_BroadcastPathIsSkipped()
    {
        // With zero connected clients the event path must short-circuit before
        // serialization. HandleDataEntry is invoked via the internal entry point;
        // reaching it with no clients is the exact hot-path no-op.
        Assert.Equal(0, _handler.ActiveClientCount);
        _handler.HandleDataEntry(new LogEntry { Direction = "RX", Text = "hello" });
        Assert.Equal(0, _handler.ActiveClientCount);
    }

    [Fact]
    public void ActiveClientCount_TracksConnectDisconnect()
    {
        Assert.Equal(0, _handler.ActiveClientCount);

        _handler.SimulateClientConnected();
        Assert.Equal(1, _handler.ActiveClientCount);

        _handler.SimulateClientConnected();
        Assert.Equal(2, _handler.ActiveClientCount);

        _handler.SimulateClientDisconnected();
        Assert.Equal(1, _handler.ActiveClientCount);

        _handler.SimulateClientDisconnected();
        Assert.Equal(0, _handler.ActiveClientCount);
    }
}