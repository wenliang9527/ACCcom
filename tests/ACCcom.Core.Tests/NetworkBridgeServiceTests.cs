using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class NetworkBridgeServiceTests
{
    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Arrange & Act
        using var service = new NetworkBridgeService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void IsConnected_InitiallyFalse()
    {
        // Arrange
        using var service = new NetworkBridgeService();

        // Act & Assert
        Assert.False(service.IsConnected);
    }

    [Fact]
    public void ConnectTcp_InvalidHost_ReturnsFalse()
    {
        // Arrange
        using var service = new NetworkBridgeService();
        var errorRaised = false;
        service.OnError += _ => errorRaised = true;

        // Act
        var result = service.ConnectTcp("invalid.host.local", 9999);

        // Assert
        Assert.False(result);
        Assert.False(service.IsConnected);
        Assert.True(errorRaised);
    }

    [Fact]
    public void Dispose_WhenNotConnected_DoesNotThrow()
    {
        // Arrange
        var service = new NetworkBridgeService();

        // Act & Assert
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Events_CanBeAttached()
    {
        // Arrange
        using var service = new NetworkBridgeService();

        // Act & Assert - attaching handlers should not throw
        service.OnDataReceived += (LogEntry _) => { };
        service.OnDisconnected += () => { };
        service.OnError += (string _) => { };
    }
}
