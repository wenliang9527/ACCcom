using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusCancellationTests
{
    [Fact]
    public async Task SendReceiveAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var virtualSerial = new VirtualSerialService();
        using var transport = new ModbusRtuTransport(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            transport.SendReceiveAsync(0x01, 0x03, [0x00, 0x00, 0x00, 0x01], 1000, cts.Token));
    }

    [Fact]
    public async Task ModbusService_ReadCancelled_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, 1000, cts.Token);
        Assert.True(result.IsError);
    }
}
