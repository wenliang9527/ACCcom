using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusServiceTests
{
    [Fact]
    public void Crc16_CalculatesCorrectly()
    {
        var data = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 };
        var crc = ModbusService.Crc16(data.AsSpan());
        Assert.Equal(0x0A84, crc);
    }

    [Fact]
    public void Crc16_EmptyData_Returns0xFFFF()
    {
        var crc = ModbusService.Crc16(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0xFFFF, crc);
    }

    [Theory]
    [InlineData("01 03 00 00 00 01", new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01 })]
    [InlineData("0A 01 FF", new byte[] { 0x0A, 0x01, 0xFF })]
    [InlineData("", new byte[0])]
    public void HexStringToBytes_ParsesCorrectly(string hex, byte[] expected)
    {
        var result = ModbusService.HexStringToBytes(hex);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ReadHoldingRegisters_WithVirtualSerial_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 03 04 00 0A 00 64 DB DA");
        });

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x02);

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, result.FunctionCode);
        Assert.Equal(5, result.Data.Length);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x0A, 0x00, 0x64 }, result.Data);
    }

    [Fact]
    public async Task ReadHoldingRegisters_Timeout_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, timeoutMs: 100);

        Assert.True(result.IsError);
        Assert.Contains("Timeout", result.ErrorMessage);
    }

    [Fact]
    public async Task WriteSingleRegister_WithVirtualSerial_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 06 00 01 01 02 58 5B");
        });

        var result = await modbus.WriteSingleRegisterAsync(0x01, 0x0001, 0x0102);

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
    }

    [Fact]
    public async Task ExceptionResponse_ParsesCorrectly()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 83 02 C0 F1");
        });

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0xFFFF, 0x01, timeoutMs: 500);

        Assert.True(result.IsError);
        Assert.Equal((byte)0x02, result.ExceptionCode!.Value);
    }

    [Fact]
    public async Task CrcMismatch_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 03 02 00 0A 00 00");
        });

        var result = await modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01, timeoutMs: 500);

        Assert.True(result.IsError);
        Assert.Contains("CRC", result.ErrorMessage);
    }

    [Fact]
    public async Task MultipleConcurrentReads_ResolveCorrectly()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 03 02 00 0A 38 43");
            await Task.Delay(20);
            virtualSerial.InjectRxData("02 03 02 00 14 FC 4B");
        });

        var t1 = modbus.ReadHoldingRegistersAsync(0x01, 0x00, 0x01);
        var t2 = modbus.ReadHoldingRegistersAsync(0x02, 0x00, 0x01);

        var results = await Task.WhenAll(t1, t2);
        Assert.False(results[0].IsError);
        Assert.False(results[1].IsError);
    }

    [Fact]
    public async Task WriteSingleCoil_On_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 05 00 01 FF 00 DD FA");
        });

        var result = await modbus.WriteSingleCoilAsync(0x01, 0x0001, true);

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
    }

    [Fact]
    public async Task WriteMultipleCoils_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var respAdu = new byte[] { 0x01, 0x0F, 0x00, 0x00, 0x00, 0x08 };
        var crc = ModbusService.Crc16(respAdu.AsSpan());
        var respHex = $"01 0F 00 00 00 08 {crc & 0xFF:X2} {crc >> 8:X2}";

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData(respHex);
        });

        var result = await modbus.WriteMultipleCoilsAsync(0x01, 0x0000, [true, false, true, false, true, false, true, false]);

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.WriteMultipleCoils, result.FunctionCode);
    }

    [Fact]
    public async Task WriteMultipleRegisters_Success()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var respAdu = new byte[] { 0x01, 0x10, 0x00, 0x01, 0x00, 0x03 };
        var crc = ModbusService.Crc16(respAdu.AsSpan());
        var respHex = $"01 10 00 01 00 03 {crc & 0xFF:X2} {crc >> 8:X2}";

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData(respHex);
        });

        var result = await modbus.WriteMultipleRegistersAsync(0x01, 0x0001, [0x0A, 0x0B, 0x0C]);

        Assert.False(result.IsError);
        Assert.Equal(0x01, result.SlaveId);
        Assert.Equal(ModbusFunctionCode.WriteMultipleRegisters, result.FunctionCode);
    }

    [Fact]
    public async Task WriteMultipleCoils_Exception_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            virtualSerial.InjectRxData("01 8F 03 71 C9");
        });

        var result = await modbus.WriteMultipleCoilsAsync(0x01, 0x0000, [true], timeoutMs: 500);

        Assert.True(result.IsError);
        Assert.Equal((byte)0x03, result.ExceptionCode!.Value);
    }

    [Fact]
    public async Task WriteMultipleRegisters_Timeout_ReturnsError()
    {
        using var virtualSerial = new VirtualSerialService();
        using var modbus = new ModbusService(virtualSerial);
        virtualSerial.Open(new SerialConfig { PortName = "COM1", BaudRate = 115200, DataBits = 8, StopBits = 1, Parity = 0 });

        var result = await modbus.WriteMultipleRegistersAsync(0x01, 0x0000, [0x0001], timeoutMs: 100);

        Assert.True(result.IsError);
        Assert.Contains("Timeout", result.ErrorMessage);
    }
}
