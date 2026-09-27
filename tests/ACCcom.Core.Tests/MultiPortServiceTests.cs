using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class MultiPortServiceTests
{
    [Fact]
    public async Task OpenPortAsync_Opens_Registers_AndIsIdempotent()
    {
        // Async twin of OpenPort: same registration contract, and a second
        // open of the same tag takes the already-open fast path.
        using var mps = new MultiPortService(() => new VirtualSerialService());
        var config = new SerialConfig { PortName = "COM10", BaudRate = 9600, DataBits = 8, StopBits = 1, Parity = 0 };

        Assert.True(await mps.OpenPortAsync("a", config));
        var instance = mps.GetPort("a");
        Assert.NotNull(instance);
        Assert.True(instance!.Service.IsOpen);
        Assert.Equal(8, instance.Service.ActiveConfig?.DataBits);
        Assert.True(await mps.OpenPortAsync("a", config));
    }

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
    public void ClosePort_null_or_empty_tag_returns_true_without_throwing()
    {
        // Dictionary.TryGetValue(null) would throw ArgumentNullException; the
        // tag-based API must treat a null/empty tag as "not open" instead.
        using var mps = new MultiPortService();
        var ex = Record.Exception(() => mps.ClosePort(null!));
        Assert.Null(ex);
        Assert.True(mps.ClosePort(""));
    }

    [Fact]
    public void SendToPort_null_or_empty_tag_returns_false_without_throwing()
    {
        using var mps = new MultiPortService();
        var ex = Record.Exception(() => mps.SendToPort(null!, "test"));
        Assert.Null(ex);
        Assert.False(mps.SendToPort(null!, "test"));
        Assert.False(mps.SendToPort("", "test"));
    }

    [Fact]
    public void Constructor_null_serviceFactory_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MultiPortService(null!));
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

    // ── Lock narrowing: a blocked port must not stall other ports ──

    [Fact]
    public async Task SendToPort_SlowSend_DoesNotBlockOtherPorts()
    {
        // Regression guard: SendToPort used to hold _lock for the whole
        // Service.Send, so a slow/blocking write on port "a" also stalled
        // GetPort/SendToPort for every other port.
        using var mps = new MultiPortService(() => new BlockingSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("a", config));
        Assert.True(mps.OpenPort("b", config));
        var a = (BlockingSerialService)mps.GetPort("a")!.Service;
        var b = (BlockingSerialService)mps.GetPort("b")!.Service;
        b.BlockSend = false; // only "a" is slow

        var slowSend = Task.Run(() => mps.SendToPort("a", "slow"));
        await a.SendEntered.WaitAsync(TimeSpan.FromSeconds(5)); // TimeoutException if never entered

        // "a"'s Send is still in flight holding no lock: "b" must respond.
        var otherOps = Task.Run(() =>
        {
            Assert.NotNull(mps.GetPort("b"));
            return mps.SendToPort("b", "fast");
        });
        var winner = await Task.WhenAny(otherOps, Task.Delay(2000));
        Assert.Same(otherOps, winner); // lock was still held �?would time out
        Assert.True(await otherOps);
        Assert.NotNull(mps.GetPort("a")); // "a" stays open while its send is in flight
    }

    [Fact]
    public async Task SendToPort_SlowSend_PortRemainsOpenUntilSendReturns()
    {
        // Companion to the responsiveness test: narrowing must not remove the
        // port from the map just because a send is in flight.
        using var mps = new MultiPortService(() => new BlockingSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("a", config));
        var a = (BlockingSerialService)mps.GetPort("a")!.Service;

        var slowSend = Task.Run(() => mps.SendToPort("a", "slow"));
        await a.SendEntered.WaitAsync(TimeSpan.FromSeconds(5)); // TimeoutException if never entered

        Assert.NotNull(mps.GetPort("a"));

        a.ReleaseSend();
        Assert.True(await slowSend.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ClosePort_SlowClose_DoesNotBlockLookups()
    {
        // ClosePort used to hold _lock across Service.Close + Dispose, so a
        // slow close blocked lookups on healthy ports too. Now the tag is
        // removed under the lock first; close happens outside it.
        using var mps = new MultiPortService(() => new BlockingSerialService());
        var config = new SerialConfig { PortName = "VIRT", BaudRate = 115200 };
        Assert.True(mps.OpenPort("a", config));
        Assert.True(mps.OpenPort("b", config));
        var a = (BlockingSerialService)mps.GetPort("a")!.Service;
        a.BlockClose = true;

        var slowClose = Task.Run(() => mps.ClosePort("a"));
        await a.CloseEntered.WaitAsync(TimeSpan.FromSeconds(5)); // TimeoutException if never entered

        var lookup = Task.Run(() => mps.GetPort("b"));
        var winner = await Task.WhenAny(lookup, Task.Delay(2000));
        Assert.Same(lookup, winner); // pre-fix this blocked behind the close

        Assert.NotNull(await lookup);
        Assert.Null(mps.GetPort("a")); // removed from the map before the slow close runs

        a.ReleaseClose();
        Assert.True(await slowClose.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// ISerialService fake whose Send/Close can block on a gate, to prove the
    /// MultiPortService lock no longer spans those calls.
    /// </summary>
    private sealed class BlockingSerialService : ISerialService
    {
#pragma warning disable CS0067 // events are interface surface; the fake never raises them
        public event Action<LogEntry>? OnDataReceived;
        public event Action<string>? OnError;
        public event Action? OnDisconnected;
        public event Action<string>? OnDeviceWait;
#pragma warning restore CS0067

        private readonly TaskCompletionSource _sendEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _sendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _closeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockSend { get; set; } = true;
        public bool BlockClose { get; set; }

        public Task SendEntered => _sendEntered.Task;
        public Task CloseEntered => _closeEntered.Task;
        public void ReleaseSend() => _sendGate.TrySetResult();
        public void ReleaseClose() => _closeGate.TrySetResult();

        public bool IsOpen { get; private set; }
        public string? CurrentPort { get; private set; }
        public int BaudRate { get; private set; }

        public bool Open(SerialConfig config)
        {
            IsOpen = true;
            CurrentPort = config.PortName;
            BaudRate = config.BaudRate;
            return true;
        }

        public bool Send(string data, bool isHex = false)
        {
            if (BlockSend)
            {
                _sendEntered.TrySetResult();
                _sendGate.Task.GetAwaiter().GetResult();
            }
            return true;
        }

        public bool SendHex(string hex) => Send(hex, isHex: true);

        public bool Close()
        {
            if (BlockClose)
            {
                _closeEntered.TrySetResult();
                _closeGate.Task.GetAwaiter().GetResult();
            }
            IsOpen = false;
            return true;
        }

        public void Dispose()
        {
            IsOpen = false;
            ReleaseSend();
            ReleaseClose();
        }
    }
}
