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
}
