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
    public void OpenPort_WithInvalidConfig_ThrowsArgumentException()
    {
        using var mps = new MultiPortService();
        var config = new SerialConfig { PortName = "", BaudRate = 0 };
        Assert.Throws<ArgumentException>(() => mps.OpenPort("test", config));
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
}
