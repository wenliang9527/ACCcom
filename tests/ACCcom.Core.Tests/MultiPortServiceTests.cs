using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class MultiPortServiceTests
{
    [Fact]
    public void SendToPort_WithoutOpen_ReturnsFalse()
    {
        using var mps = new MultiPortService();
        var result = mps.SendToPort("nonexistent", "test");
        Assert.False(result);
    }

    [Fact]
    public void ClosePort_WithoutOpen_ReturnsTrue()
    {
        using var mps = new MultiPortService();
        var result = mps.ClosePort("nonexistent");
        Assert.True(result);
    }

    [Fact]
    public void CloseAll_WhenEmpty_DoesNotThrow()
    {
        var mps = new MultiPortService();
        var ex = Record.Exception(() => mps.CloseAll());
        Assert.Null(ex);
        mps.Dispose();
    }

    [Fact]
    public void OpenPort_WithInvalidConfig_ReturnsFalse()
    {
        // A bad config (empty port, zero baud) used to surface as an
        // ArgumentException propagating from SerialService.Open; it now fails
        // cleanly as a bool return, matching the OpenPort contract.
        using var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "", BaudRate = 0 };
        Assert.False(mps.OpenPort("test", config));
    }

    [Fact]
    public void OpenPort_null_tag_or_config_returns_false()
    {
        using var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "COM1", BaudRate = 115200 };

        // A null tag would throw ArgumentNullException from ContainsKey; both
        // must fail cleanly as bool returns.
        Assert.False(mps.OpenPort(null, config));
        Assert.False(mps.OpenPort("tag", null));
    }

    [Fact]
    public void Ports_InitiallyEmpty()
    {
        using var mps = new MultiPortService();
        Assert.Empty(mps.Ports);
    }

    [Fact]
    public void ReOpenSameTag_ReturnsExistingStatus()
    {
        using var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "COM99", BaudRate = 115200 };
        var first = mps.OpenPort("tag1", config);
        var second = mps.OpenPort("tag1", config);
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetPorts_AfterFailedOpen_RemainsEmpty()
    {
        using var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "COM99", BaudRate = 115200 };
        var ex = Record.Exception(() => mps.OpenPort("sensor1", config));
        Assert.Empty(mps.Ports);
    }

    [Fact]
    public void Events_CanBeAttached()
    {
        using var mps = new MultiPortService();
        mps.OnDataReceived += (LogEntry _) => { };
        mps.OnPortError += (string _, string _) => { };
        mps.OnPortDisconnected += (string _) => { };
    }

    [Fact]
    public void GetPort_ReturnsPort_WhenOpen_ElseNull()
    {
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };

        Assert.Null(mps.GetPort("unknown"));
        Assert.True(mps.OpenPort("sensor", config));

        var port = mps.GetPort("sensor");
        Assert.NotNull(port);
        Assert.Equal("sensor", port!.Tag);
        Assert.True(port.Service.IsOpen);
        Assert.Null(mps.GetPort(""));
        Assert.Null(mps.GetPort(null));
    }

    [Fact]
    public void Ports_SnapshotDoesNotThrow_WhenClosedDuringEnumerate()
    {
        // Ports returns a copy, so closing while enumerating the snapshot is
        // safe (a bare dictionary reference would throw InvalidOperationException).
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("a", config));
        Assert.True(mps.OpenPort("b", config));

        var snapshot = mps.Ports;
        Assert.Equal(2, snapshot.Count);
        // Close both AFTER taking the snapshot: the snapshot still enumerates.
        mps.ClosePort("a");
        mps.ClosePort("b");
        Assert.Equal(2, snapshot.Count); // snapshot is a copy
    }

    [Fact]
    public void Dispose_ClosesAllPorts()
    {
        var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "COM99", BaudRate = 115200 };
        mps.OpenPort("p1", config);
        mps.OpenPort("p2", config);
        mps.Dispose();
        Assert.Empty(mps.Ports);
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        var mps = new MultiPortService();
        mps.Dispose();
        var ex = Record.Exception(() => mps.Dispose());
        Assert.Null(ex);
    }

    // ── Real routing via injected virtual serial services ──

    [Fact]
    public void OpenPort_WithVirtualSerial_OpensAndLists()
    {
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };

        Assert.True(mps.OpenPort("sensor", config));
        Assert.Single(mps.Ports);
        Assert.True(mps.Ports["sensor"].Service.IsOpen);
        Assert.Equal("sensor", mps.Ports["sensor"].Tag);
    }

    [Fact]
    public void SendToPort_RoutesToCorrectVirtualSerial()
    {
        // Two ports; send on each; each virtual serial records its own TX data.
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("a", config));
        Assert.True(mps.OpenPort("b", config));

        Assert.True(mps.SendToPort("a", "hello-a"));
        Assert.True(mps.SendToPort("b", "hello-b"));

        var a = (VirtualSerialService)mps.Ports["a"].Service;
        var b = (VirtualSerialService)mps.Ports["b"].Service;
        Assert.Contains("hello-a", a.GetSentData().Select(e => e.Text));
        Assert.DoesNotContain("hello-b", a.GetSentData().Select(e => e.Text));
        Assert.Contains("hello-b", b.GetSentData().Select(e => e.Text));
        Assert.DoesNotContain("hello-a", b.GetSentData().Select(e => e.Text));
    }

    [Fact]
    public void InjectRxData_TagsEntriesWithPortTag()
    {
        // RX data injected on a port must surface with that port's tag.
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("pump", config));

        var received = new List<LogEntry>();
        mps.OnDataReceived += received.Add;

        ((VirtualSerialService)mps.Ports["pump"].Service).InjectRxData("AA BB CC");

        var entry = Assert.Single(received);
        Assert.Equal("pump", entry.PortTag);
        Assert.Equal("RX", entry.Direction);
    }

    [Fact]
    public void ClosePort_RemovesAndDisposes()
    {
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("x", config));

        Assert.True(mps.ClosePort("x"));
        Assert.Empty(mps.Ports);

        // Sending to the closed port fails.
        Assert.False(mps.SendToPort("x", "data"));
    }

    [Fact]
    public void MultiplePorts_IsolatedDataStreams()
    {
        // Each port's RX feed must not leak into the other port's service.
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("one", config));
        Assert.True(mps.OpenPort("two", config));

        var events = new List<LogEntry>();
        mps.OnDataReceived += events.Add;

        ((VirtualSerialService)mps.Ports["one"].Service).InjectRxData("01");
        ((VirtualSerialService)mps.Ports["two"].Service).InjectRxData("02");

        Assert.Equal(2, events.Count);
        Assert.Equal("one", events[0].PortTag);
        Assert.Equal("two", events[1].PortTag);
        Assert.Equal("01", events[0].RawHex.Replace(" ", ""));
        Assert.Equal("02", events[1].RawHex.Replace(" ", ""));
    }
}
