using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class SerialConnectionManagerTests
{
    [Fact]
    public async Task ToggleConnection_WhenClosed_OpensPort()
    {
        using var serial = new VirtualSerialService();
        using var mgr = new SerialConnectionManager();
        var config = new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 };
        var result = await mgr.ToggleConnection(serial, config, currentlyOpen: false);
        Assert.True(result);
        Assert.True(serial.IsOpen);
    }

    [Fact]
    public async Task ToggleConnection_WhenOpen_ClosesPort()
    {
        using var serial = new VirtualSerialService();
        using var mgr = new SerialConnectionManager();
        var config = new SerialConfig { PortName = "VIRTUAL", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 };
        await mgr.ToggleConnection(serial, config, currentlyOpen: false);
        var result = await mgr.ToggleConnection(serial, config, currentlyOpen: true);
        Assert.False(result);
        Assert.False(serial.IsOpen);
    }

    [Fact]
    public async Task ToggleConnection_WithNullConfig_ReturnsFalse()
    {
        using var serial = new VirtualSerialService();
        using var mgr = new SerialConnectionManager();
        var result = await mgr.ToggleConnection(serial, null, currentlyOpen: false);
        Assert.False(result);
        Assert.False(serial.IsOpen);
    }

    [Fact]
    public void StartTracking_RaisesDurationChanged()
    {
        using var mgr = new SerialConnectionManager();
        string? captured = null;
        mgr.DurationChanged += d => captured = d;
        mgr.StartTracking();
        Assert.StartsWith("00:00:0", captured ?? "");
    }

    [Fact]
    public void StopTracking_StopsDurationUpdates()
    {
        using var mgr = new SerialConnectionManager();
        int count = 0;
        mgr.DurationChanged += _ => count++;
        mgr.StartTracking();
        Thread.Sleep(100);
        mgr.StopTracking();
        var afterStop = count;
        Thread.Sleep(200);
        Assert.Equal(afterStop, count);
    }

    [Fact]
    public void DoubleStartTracking_RaisesDurationChangedAgain()
    {
        using var mgr = new SerialConnectionManager();
        int count = 0;
        mgr.DurationChanged += _ => count++;
        mgr.StartTracking();
        Thread.Sleep(50);
        mgr.StartTracking();
        Thread.Sleep(50);
        mgr.StopTracking();
        Assert.True(count >= 2);
    }

    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        var mgr = new SerialConnectionManager();
        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task ToggleConnection_CloseWithoutOpen_ReturnsFalse()
    {
        using var serial = new VirtualSerialService();
        using var mgr = new SerialConnectionManager();
        var result = await mgr.ToggleConnection(serial, null, currentlyOpen: true);
        Assert.False(result);
    }

    [Fact]
    public async Task ToggleConnection_NullSerial_ReturnsFalse()
    {
        using var mgr = new SerialConnectionManager();

        // A null service must not NRE in the close/open path; it fails cleanly.
        Assert.False(await mgr.ToggleConnection(null, null, currentlyOpen: false));
        Assert.False(await mgr.ToggleConnection(null, null, currentlyOpen: true));
    }
}
