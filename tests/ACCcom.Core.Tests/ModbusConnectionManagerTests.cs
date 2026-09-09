using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ModbusConnectionManagerTests
{
    [Fact]
    public void CreateAsciiConnection_registers_connection()
    {
        using var manager = new ModbusConnectionManager();
        using var serial = new VirtualSerialService();

        var svc = manager.CreateAsciiConnection("ascii", serial);

        Assert.NotNull(svc);
        Assert.Same(svc, manager.GetService("ascii"));
    }

    [Fact]
    public void CreateAsciiConnection_exposed_with_ascii_description()
    {
        using var manager = new ModbusConnectionManager();
        using var serial = new VirtualSerialService();

        manager.CreateAsciiConnection("ascii", serial);

        var active = manager.GetActiveConnections();
        Assert.True(active.TryGetValue("ascii", out var description));
        Assert.Equal("ASCII", description);
    }

    [Fact]
    public void CreateAsciiConnection_duplicate_id_throws()
    {
        using var manager = new ModbusConnectionManager();
        using var serial = new VirtualSerialService();

        manager.CreateAsciiConnection("ascii", serial);

        Assert.Throws<InvalidOperationException>(() => manager.CreateAsciiConnection("ascii", serial));
    }

    [Fact]
    public void CreateAsciiConnection_does_not_collide_with_default_rtu()
    {
        using var manager = new ModbusConnectionManager();
        using var serial = new VirtualSerialService();

        manager.GetDefaultService(serial);
        var svc = manager.CreateAsciiConnection("ascii", serial);

        Assert.NotNull(svc);
        Assert.Equal(2, manager.GetActiveConnections().Count);
    }

    [Fact]
    public void Null_arguments_throw_argument_null()
    {
        using var manager = new ModbusConnectionManager();
        using var serial = new VirtualSerialService();

        // A null serial would NRE inside the transport constructor with an
        // unclear message; fail at the entry point.
        Assert.Throws<ArgumentNullException>(() => manager.GetDefaultService(null!));
        Assert.Throws<ArgumentNullException>(() => manager.CreateAsciiConnection("a", null!));
        Assert.Throws<ArgumentNullException>(() => manager.CreateAsciiConnection(null!, serial));
        Assert.Throws<ArgumentNullException>(() => manager.CreateTcpConnection("t", null!, 502));
        Assert.Throws<ArgumentNullException>(() => manager.CreateTcpConnection(null!, "host", 502));
    }
}
