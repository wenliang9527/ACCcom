using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

/// <summary>
/// Hardware-free <see cref="SerialService"/> basics: defaults, open-failure,
/// close/dispose lifecycle and the SendHex passthrough. Anything needing a
/// real port is covered by the virtual-serial integration tests instead.
/// </summary>
public class SerialServiceBasicsTests
{
    [Fact]
    public void FreshInstance_ReportsClosedDefaults()
    {
        using var serial = new SerialService();

        Assert.False(serial.IsOpen);
        Assert.Null(serial.CurrentPort);
        Assert.Equal(0, serial.BaudRate);
    }

    [Fact]
    public void Close_WithoutOpen_ReturnsTrue()
    {
        using var serial = new SerialService();

        Assert.True(serial.Close());
        Assert.False(serial.IsOpen);
    }

    [Fact]
    public void Close_AfterDispose_ReturnsTrue()
    {
        var serial = new SerialService();
        serial.Dispose();

        Assert.True(serial.Close());
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var serial = new SerialService();

        var ex = Record.Exception(() =>
        {
            serial.Dispose();
            serial.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void SendHex_InvalidHex_ReportsValidationError()
    {
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(serial.SendHex("ZZ"));

        Assert.Contains("invalid hex", Assert.Single(errors));
    }

    [Fact]
    public void SendHex_ValidHexWithoutPort_ReportsClosedPort()
    {
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;

        Assert.False(serial.SendHex("AA BB"));

        Assert.Contains("serial port not open", Assert.Single(errors));
    }

    [Fact]
    public void Open_NonexistentPort_ReturnsFalseAndReportsError()
    {
        // Arrange: a port name that cannot exist on any machine.
        using var serial = new SerialService();
        var errors = new List<string>();
        serial.OnError += errors.Add;
        var config = new SerialConfig { PortName = "COM__DOES_NOT_EXIST__" };

        // Act: 2 retries x 500ms inside Open, so this takes ~1s.
        var ok = serial.Open(config);

        // Assert
        Assert.False(ok);
        Assert.False(serial.IsOpen);
        Assert.Contains("Open failed after 3 attempts", Assert.Single(errors));
    }

    [Fact]
    public void Open_NonExistentPort_RecordsLastOpenErrorDiagnosis()
    {
        using var serial = new SerialService();
        Assert.Null(serial.LastOpenError);

        // Well-formed COM name that is absent from the system: .NET throws
        // IOException "The port 'COM99' does not exist." (a non-COM name
        // would take the ArgumentException formatting path instead.)
        var ok = serial.Open(new SerialConfig { PortName = "COM99" });

        Assert.False(ok);
        // The stored detail is what turns OPEN_FAILED into a diagnosis. The
        // exact exception for an absent port varies by Windows release
        // (FileNotFoundException "Could not find file" vs IOException "does
        // not exist") — assert the family, not the wording.
        Assert.NotNull(serial.LastOpenError);
        Assert.True(
            serial.LastOpenError.Contains("FileNotFound", StringComparison.Ordinal) ||
            serial.LastOpenError.Contains("does not exist", StringComparison.OrdinalIgnoreCase),
            serial.LastOpenError);
    }

    [Fact]
    public void GetAvailablePorts_ReturnsNonNull()
    {
        var ports = SerialService.GetAvailablePorts();

        Assert.NotNull(ports);
    }
}
