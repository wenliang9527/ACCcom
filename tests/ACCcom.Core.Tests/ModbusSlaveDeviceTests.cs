using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusSlaveDeviceTests
{
    private static ModbusSlaveDevice CreateDevice(byte slaveId = 0x01)
        => new(slaveId, coils: 16, discreteInputs: 16, holdingRegisters: 16, inputRegisters: 16);

    [Fact]
    public void FC01_ReadCoils_ReturnsCorrectBitPattern()
    {
        var device = CreateDevice();
        device.SetCoil(0, true); device.SetCoil(2, true); device.SetCoil(7, true);
        var resp = device.HandleRequest(0x01, [0x00, 0x00, 0x00, 0x08]);
        Assert.Equal(1, resp[0]);
        Assert.Equal(0b1000_0101, resp[1]);
    }

    [Fact]
    public void FC01_ReadCoils_AddressOutOfRange_ReturnsException()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x01, [0x00, 0x20, 0x00, 0x01]);
        Assert.Equal(0x81, resp[0]); Assert.Equal(0x02, resp[1]);
    }

    [Fact]
    public void FC02_ReadDiscreteInputs_ReturnsCorrectBitPattern()
    {
        var device = CreateDevice();
        device.SetDiscreteInput(1, true); device.SetDiscreteInput(5, true);
        var resp = device.HandleRequest(0x02, [0x00, 0x00, 0x00, 0x08]);
        Assert.Equal(1, resp[0]); Assert.Equal(0b0010_0010, resp[1]);
    }

    [Fact]
    public void FC02_ReadDiscreteInputs_CountZero_ReturnsException()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x02, [0x00, 0x00, 0x00, 0x00]);
        Assert.Equal(0x82, resp[0]); Assert.Equal(0x03, resp[1]);
    }

    [Fact]
    public void FC03_ReadHoldingRegisters_ReturnsValues()
    {
        var device = CreateDevice();
        device.SetHoldingRegister(0, 0x0A); device.SetHoldingRegister(1, 0x64); device.SetHoldingRegister(2, 0x0100);
        var resp = device.HandleRequest(0x03, [0x00, 0x00, 0x00, 0x03]);
        Assert.Equal(6, resp[0]);
        Assert.Equal([0x00, 0x0A, 0x00, 0x64, 0x01, 0x00], resp[1..]);
    }

    [Fact]
    public void FC04_ReadInputRegisters_ReturnsValues()
    {
        var device = CreateDevice(); device.SetInputRegister(5, 0x1234);
        var resp = device.HandleRequest(0x04, [0x00, 0x05, 0x00, 0x01]);
        Assert.Equal(2, resp[0]); Assert.Equal(0x12, resp[1]); Assert.Equal(0x34, resp[2]);
    }

    [Fact]
    public void FC05_WriteSingleCoil_On_SetsCoil()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x05, [0x00, 0x03, 0xFF, 0x00]);
        Assert.Equal([0x00, 0x03, 0xFF, 0x00], resp);
        Assert.True(device.GetCoil(3));
    }

    [Fact]
    public void FC05_WriteSingleCoil_Off_ClearsCoil()
    {
        var device = CreateDevice(); device.SetCoil(3, true);
        var resp = device.HandleRequest(0x05, [0x00, 0x03, 0x00, 0x00]);
        Assert.Equal([0x00, 0x03, 0x00, 0x00], resp);
        Assert.False(device.GetCoil(3));
    }

    [Fact]
    public void FC06_WriteSingleRegister_SetsValue()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x06, [0x00, 0x05, 0xAB, 0xCD]);
        Assert.Equal([0x00, 0x05, 0xAB, 0xCD], resp);
        Assert.Equal(0xABCD, device.GetHoldingRegister(5));
    }

    [Fact]
    public void FC0F_WriteMultipleCoils_SetsBits()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x0F, [0x00, 0x02, 0x00, 0x0A, 0x02, 0b1010_0101, 0b01]);
        Assert.Equal([0x00, 0x02, 0x00, 0x0A], resp);
        Assert.True(device.GetCoil(2)); Assert.False(device.GetCoil(3));
        Assert.True(device.GetCoil(4)); Assert.True(device.GetCoil(10));
    }

    [Fact]
    public void FC10_WriteMultipleRegisters_SetsValues()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x10, [0x00, 0x02, 0x00, 0x03, 0x06, 0x00, 0x0A, 0x00, 0x64, 0x01, 0x00]);
        Assert.Equal([0x00, 0x02, 0x00, 0x03], resp);
        Assert.Equal(0x000A, device.GetHoldingRegister(2));
        Assert.Equal(0x0064, device.GetHoldingRegister(3));
    }

    [Fact]
    public void FC16_MaskWriteRegister_AppliesAndOr()
    {
        var device = CreateDevice(); device.SetHoldingRegister(0, 0x1234);
        var resp = device.HandleRequest(0x16, [0x00, 0x00, 0x00, 0xFF, 0x01, 0x00]);
        Assert.Equal([0x00, 0x00, 0x00, 0xFF, 0x01, 0x00], resp);
        Assert.Equal(0x0134, device.GetHoldingRegister(0));
    }

    [Fact]
    public void FC17_ReadWriteMultipleRegisters_WritesThenReads()
    {
        var device = CreateDevice();
        device.SetHoldingRegister(0, 0x1111); device.SetHoldingRegister(1, 0x2222);
        var resp = device.HandleRequest(0x17, [0x00, 0x00, 0x00, 0x02, 0x00, 0x05, 0x00, 0x02, 0x04, 0xAA, 0xBB, 0xCC, 0xDD]);
        Assert.Equal(4, resp[0]);
        Assert.Equal(0x11, resp[1]); Assert.Equal(0x11, resp[2]);
        Assert.Equal(0x22, resp[3]); Assert.Equal(0x22, resp[4]);
        Assert.Equal(0xAABB, device.GetHoldingRegister(5));
        Assert.Equal(0xCCDD, device.GetHoldingRegister(6));
    }

    [Fact]
    public void HandleRequest_UnsupportedFunction_ReturnsIllegalFunction()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x7F, []);
        Assert.Equal(0xFF, resp[0]); Assert.Equal(0x01, resp[1]);
    }

    [Fact]
    public void HandleRequest_MaxQuantityExceeded_ReturnsIllegalDataValue()
    {
        var device = CreateDevice();
        var resp = device.HandleRequest(0x01, [0x00, 0x00, 0x07, 0xD0]);
        Assert.Equal(0x81, resp[0]); Assert.Equal(0x03, resp[1]);
    }
}
