using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class ModbusFunctionCodeExtensionTests
{
    [Fact]
    public void MaskWriteRegister_BuildsCorrectPdu()
    {
        var pdu = ModbusService.BuildMaskWriteRequest(0x0001, 0x00FF, 0x0100);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0xFF, 0x01, 0x00 }, pdu);
    }

    [Fact]
    public void ReadWriteMultipleRegisters_BuildsCorrectPdu()
    {
        var pdu = ModbusService.BuildReadWriteRegistersRequest(0x0000, 0x0003, 0x0100, [0x000A, 0x000B]);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x02, 0x04, 0x00, 0x0A, 0x00, 0x0B }, pdu);
    }

    [Fact]
    public void MaskWriteRegister_PduLength_6()
    {
        var pdu = ModbusService.BuildMaskWriteRequest(0x1234, 0xABCD, 0x5678);
        Assert.Equal(6, pdu.Length);
    }

    [Fact]
    public void ReadWriteMultipleRegisters_EmptyWriteValues_BuildsMinimalPdu()
    {
        var pdu = ModbusService.BuildReadWriteRegistersRequest(0, 0, 0, []);
        Assert.Equal(9, pdu.Length);
        Assert.Equal(0, pdu[6]); // writeCount high
        Assert.Equal(0, pdu[7]); // writeCount low = 0
        Assert.Equal(0, pdu[8]); // byteCount = 0
    }
}
