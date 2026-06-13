using System.Reflection;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class AutoBaudDetectorTests
{
    [Fact]
    public void Class_ImplementsIDisposable()
    {
        // Arrange & Act
        using var detector = new AutoBaudDetector();

        // Assert
        Assert.IsAssignableFrom<IDisposable>(detector);
    }

    [Fact]
    public void DetectAsync_MethodExists_WithCorrectSignature()
    {
        // Arrange
        var type = typeof(AutoBaudDetector);

        // Act
        var method = type.GetMethod("DetectAsync");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<int>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    [Fact]
    public void TryBaudRateAsync_MethodExists_WithCorrectSignature()
    {
        // Arrange
        var type = typeof(AutoBaudDetector);

        // Act
        var method = type.GetMethod("TryBaudRateAsync");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
    }

    [Fact]
    public void CommonBaudRates_FieldExists_AndContainsStandardRates()
    {
        // Arrange
        var type = typeof(AutoBaudDetector);
        var field = type.GetField("CommonRates",
            BindingFlags.NonPublic | BindingFlags.Static);

        // Act
        Assert.NotNull(field);
        var value = (int[]?)field.GetValue(null);

        // Assert
        Assert.NotNull(value);
        Assert.NotEmpty(value);
        Assert.Contains(9600, value);
        Assert.Contains(115200, value);
        Assert.Contains(57600, value);
        Assert.Contains(38400, value);
        Assert.Contains(19200, value);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Arrange
        var detector = new AutoBaudDetector();

        // Act & Assert
        var exception = Record.Exception(() => detector.Dispose());
        Assert.Null(exception);
    }
}
