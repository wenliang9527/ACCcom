using System.IO.Ports;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

/// <summary>Adopt/drop decision of the reconnect swap
/// (<see cref="SerialService.AdoptReconnectedPort"/>) and the async close
/// contract. Neither needs hardware: the adopt seam takes an
/// already-constructed SerialPort and only inspects the token, and CloseAsync
/// on a never-opened service must behave exactly like Close.</summary>
public class SerialServiceCloseAdoptTests
{
    [Fact]
    public void Adopt_cancelled_token_drops_fresh_port_without_swapping()
    {
        using var serial = new SerialService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fresh = new SerialPort("COM_AUDIT_TEST");

        serial.AdoptReconnectedPort(cts.Token, fresh);

        // No swap: after Close() cancelled the reconnect, the user's close owns
        // the port lifecycle — a swap here would resurrect a closed port.
        Assert.Null(serial.CurrentPort);
        // The fresh port is dropped: not open, and (SerialPort.Dispose is not
        // observable via IsOpen on .NET 8) a second Dispose stays a no-op.
        Assert.False(fresh.IsOpen);
        fresh.Dispose();
    }

    [Fact]
    public async Task Adopt_live_token_swaps_port_and_announces()
    {
        using var serial = new SerialService();
        using var cts = new CancellationTokenSource();
        LogEntry? announced = null;
        serial.OnDataReceived += e => announced = e;

        var fresh = new SerialPort("COM_AUDIT_TEST");
        serial.AdoptReconnectedPort(cts.Token, fresh);

        Assert.Equal("COM_AUDIT_TEST", serial.CurrentPort);
        // The announcement goes through DispatchRx; an entry leaves the channel
        // (Count → 0) slightly BEFORE the drain's handler runs, so poll for the
        // handler to have fired rather than asserting right after the drain.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (announced == null && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.NotNull(announced);
        Assert.Contains("Auto reconnect", announced!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseAsync_without_open_port_returns_true()
    {
        using var serial = new SerialService();
        Assert.True(await serial.CloseAsync());
    }

    [Fact]
    public async Task Concurrent_sends_on_closed_port_all_fail_cleanly()
    {
        // Without hardware every write is rejected up front (port not open), so
        // this proves the send path stays exception-free under 16 racing
        // senders — the write gate itself (interleaving protection) can only be
        // exercised against a real port; 待真机验证.
        using var serial = new SerialService();
        var results = new bool[16];
        await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            await Task.Yield();
            results[i] = serial.Send($"payload-{i}");
        }));
        Assert.All(results, r => Assert.False(r));
    }
}
