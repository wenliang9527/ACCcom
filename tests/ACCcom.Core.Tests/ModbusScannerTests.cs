using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusScannerTests
{
    /// <summary>Fake transport that answers holding-register reads for a fixed
    /// set of online slave IDs and times out for everyone else. Lets us test the
    /// scanner without a physical bus.</summary>
    private sealed class FakeTransport : IModbusTransport
    {
        private readonly HashSet<byte> _online;
        private readonly ushort _firstRegisterValue;

        public FakeTransport(IEnumerable<byte> online, ushort firstRegisterValue = 0x1234)
        {
            _online = new HashSet<byte>(online);
            _firstRegisterValue = firstRegisterValue;
        }

        public Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
        {
            if (!_online.Contains(slaveId) || functionCode != (byte)ModbusFunctionCode.ReadHoldingRegisters)
                throw new TimeoutException($"Slave {slaveId} not responding");

            // RTU response without CRC: [slaveId, func, byteCount, data...]
            var valueBytes = new[] { (byte)(_firstRegisterValue >> 8), (byte)(_firstRegisterValue & 0xFF) };
            var resp = new byte[1 + 1 + 1 + valueBytes.Length];
            resp[0] = slaveId;
            resp[1] = functionCode;
            resp[2] = (byte)valueBytes.Length;
            Array.Copy(valueBytes, 0, resp, 3, valueBytes.Length);
            return Task.FromResult(resp);
        }

        public void Dispose() { }
    }

    private static ModbusService MakeService(IModbusTransport transport) => new(transport);

    [Fact]
    public async Task ScanAsync_FindsOnlyOnlineSlaves()
    {
        // 0x03 and 0x10 respond; the rest of the 1..3 range do not.
        var transport = new FakeTransport(new byte[] { 0x03, 0x10 });
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);

        var results = await scanner.ScanAsync(0x01, 0x10, timeoutMs: 200);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsOnline));
        Assert.Contains(results, r => r.SlaveId == 0x03);
        Assert.Contains(results, r => r.SlaveId == 0x10);
    }

    [Fact]
    public async Task ScanAsync_PopulatesFirstRegisterValue()
    {
        var transport = new FakeTransport(new byte[] { 0x05 }, firstRegisterValue: 0xBEEF);
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);

        var results = await scanner.ScanAsync(0x01, 0x05, timeoutMs: 200);

        var found = Assert.Single(results);
        Assert.Equal(0x05, found.SlaveId);
        Assert.Equal(0xBEEF, found.FirstRegisterValue);
    }

    [Fact]
    public async Task ScanAsync_NoDevices_ReturnsEmpty()
    {
        var transport = new FakeTransport(Array.Empty<byte>());
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);

        var results = await scanner.ScanAsync(0x01, 0x03, timeoutMs: 200);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_RaisesDeviceFoundAndCompletedEvents()
    {
        var transport = new FakeTransport(new byte[] { 0x02 });
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);

        var found = new List<byte>();
        var completed = false;
        scanner.OnDeviceFound += r => found.Add(r.SlaveId);
        scanner.OnScanCompleted += () => completed = true;

        await scanner.ScanAsync(0x01, 0x02, timeoutMs: 200);

        Assert.Equal(new byte[] { 0x02 }, found);
        Assert.True(completed);
    }

    [Fact]
    public async Task ScanAsync_Cancellation_StopsEarly()
    {
        var transport = new FakeTransport(Array.Empty<byte>());
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scanner.ScanAsync(0x01, 0x05, timeoutMs: 500, ct: cts.Token));
    }

    [Fact]
    public async Task StopScan_CancelsRunningScan()
    {
        // A "hanging" transport that never returns lets us stop mid-scan.
        var transport = new NeverRespondingTransport();
        using var modbus = MakeService(transport);
        using var scanner = new ModbusScanner(modbus);

        var task = scanner.ScanAsync(0x01, 0x05, timeoutMs: 10000);
        await Task.Delay(30);
        scanner.StopScan();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class NeverRespondingTransport : IModbusTransport
    {
        public async Task<byte[]> SendReceiveAsync(byte slaveId, byte functionCode, byte[] pdu, int timeoutMs, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromDays(1), ct);
            return [];
        }

        public void Dispose() { }
    }
}