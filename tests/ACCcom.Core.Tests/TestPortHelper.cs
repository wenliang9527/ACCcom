using System.Net;
using System.Net.Sockets;

namespace ACCcom.Core.Tests;

/// <summary>
/// Allocates an ephemeral loopback port that was free at bind time. Using a
/// dynamic port (rather than a hard-coded one) keeps HTTP/Modbus listener tests
/// from colliding when xUnit runs different test classes in parallel.
/// </summary>
public static class TestPortHelper
{
    public static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
