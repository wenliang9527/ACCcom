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
        // Field is now public so the UI can iterate it without reflection.
        var field = type.GetField("CommonRates",
            BindingFlags.Public | BindingFlags.Static);

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
    public async Task DetectAsync_NoMatch_ReturnsZero()
    {
        // A non-existent port name can never be opened, so every baud probe
        // fails silently and DetectAsync returns 0 without throwing.
        using var detector = new AutoBaudDetector();
        using var cts = new CancellationTokenSource();

        var result = await detector.DetectAsync("COM_nonexistent_port", cts.Token);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task DetectAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var detector = new AutoBaudDetector();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            detector.DetectAsync("COM_nonexistent_port", cts.Token));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var detector = new AutoBaudDetector();
        detector.Dispose();
        var exception = Record.Exception(() => detector.Dispose());
        Assert.Null(exception);
    }
}
