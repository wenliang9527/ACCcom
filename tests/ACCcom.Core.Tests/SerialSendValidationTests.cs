using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class SerialSendValidationTests
{
    [Theory]
    [InlineData("ZZ")]
    [InlineData("AA B")]
    [InlineData("AA\tZ1")]
    [InlineData("AA\u00a0BB")]
    public void Send_InvalidHex_ReportsValidationErrorBeforePortAccess(string payload)
    {
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(serial.Send(payload, true));

        Assert.Contains("invalid hex", Assert.Single(errors));
        Assert.False(serial.IsOpen);
    }

    [Theory]
    [InlineData("AA BB CC")]
    [InlineData("aA\tBB\r\ncc")]
    [InlineData("\t\r\n")]
    public void Send_ValidHexWithoutPort_ReportsClosedPort(string payload)
    {
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(serial.Send(payload, true));

        Assert.Contains("serial port not open", Assert.Single(errors));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SendAsync_NullOrEmpty_ReturnsFalseWithoutErrorEvent(string? payload)
    {
        // Same up-front rejection as Send: the conversion would throw inside
        // the retry loop and burn a 500ms delay first.
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(await serial.SendAsync(payload));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SendAsync_ClosedPort_ReturnsFalseFast()
    {
        // The closed-port path must not wait out the retry delay — a UI caller
        // awaits this, and the failure is known before any write is attempted.
        using var serial = new SerialService();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(await serial.SendAsync("hello"));

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 400,
            $"closed-port SendAsync took {sw.ElapsedMilliseconds}ms — it is waiting on the retry delay");
    }

    [Fact]
    public async Task SendAsync_InvalidHex_ReportsTheSameValidationError()
    {
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(await serial.SendAsync("ZZ", isHex: true));

        Assert.Contains("invalid hex", Assert.Single(errors));
    }

    [Fact]
    public async Task SendAsync_ThroughTheInterface_default_wraps_sync_send()
    {
        // ISerialService.SendAsync has a default implementation wrapping the
        // sync Send; implementations without retry sleeps (virtual serial,
        // network bridge) inherit it. The default must forward to the
        // implementation's own contract — note VirtualSerialService is
        // deliberately LENIENT with hex (it logs the compacted original rather
        // than failing), unlike the strict SerialService.
        ISerialService serial = new VirtualSerialService();
        serial.Open(new ACCcom.Core.Models.SerialConfig { PortName = "VIRTUAL", BaudRate = 115200 });

        Assert.False(await serial.SendAsync(null));
        Assert.True(await serial.SendAsync("AA BB", isHex: true));
        Assert.True(await serial.SendAsync("hello"));
    }
}
