using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"acccom_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string GetTempSettingsPath() => Path.Combine(_tempDir, "settings.json");

    [Fact]
    public void Load_WithNoFile_ReturnsDefaultValues()
    {
        // Arrange
        var service = new SettingsService(GetTempSettingsPath());

        // Act
        var settings = service.Load();

        // Assert
        Assert.True(double.IsNaN(settings.WindowX));
        Assert.True(double.IsNaN(settings.WindowY));
        Assert.True(double.IsNaN(settings.WindowWidth));
        Assert.True(double.IsNaN(settings.WindowHeight));
        Assert.False(settings.IsDarkTheme);
        Assert.Equal("", settings.LastPort);
        Assert.Equal(115200, settings.LastBaudRate);
        Assert.Equal(8, settings.LastDataBits);
        Assert.False(settings.IsHexSend);
        Assert.False(settings.IsHexDisplayRx);
        Assert.False(settings.IsHexDisplayTx);
        Assert.True(settings.EnableRxTimestamp);
        Assert.True(settings.EnableTxTimestamp);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesAllValues()
    {
        // Arrange
        var path = GetTempSettingsPath();
        var service = new SettingsService(path);
        var original = new AppSettings
        {
            WindowX = 100,
            WindowY = 200,
            WindowWidth = 800,
            WindowHeight = 600,
            IsDarkTheme = true,
            LastPort = "COM3",
            LastBaudRate = 9600,
            LastDataBits = 7,
            IsHexSend = true,
            IsHexDisplayRx = true,
            IsHexDisplayTx = false,
            EnableRxTimestamp = false,
            EnableTxTimestamp = false
        };

        // Act
        service.Save(original);
        var loaded = service.Load();

        // Assert
        Assert.Equal(100, loaded.WindowX);
        Assert.Equal(200, loaded.WindowY);
        Assert.Equal(800, loaded.WindowWidth);
        Assert.Equal(600, loaded.WindowHeight);
        Assert.True(loaded.IsDarkTheme);
        Assert.Equal("COM3", loaded.LastPort);
        Assert.Equal(9600, loaded.LastBaudRate);
        Assert.Equal(7, loaded.LastDataBits);
        Assert.True(loaded.IsHexSend);
        Assert.True(loaded.IsHexDisplayRx);
        Assert.False(loaded.IsHexDisplayTx);
        Assert.False(loaded.EnableRxTimestamp);
        Assert.False(loaded.EnableTxTimestamp);
    }

    [Fact]
    public void Load_WithCorruptedJson_ReturnsDefaults()
    {
        // Arrange
        var path = GetTempSettingsPath();
        File.WriteAllText(path, "{ not valid json !!!");
        var service = new SettingsService(path);

        // Act
        var settings = service.Load();

        // Assert
        Assert.True(double.IsNaN(settings.WindowX));
        Assert.Equal("", settings.LastPort);
        Assert.Equal(115200, settings.LastBaudRate);
    }

    [Fact]
    public void DefaultSettingsPath_IsUnderAppBaseDirectory()
    {
        // Arrange
        var expected = Path.Combine(AppContext.BaseDirectory, "settings.json");

        // Act - use default path (null)
        var service = new SettingsService();

        // Assert - save to verify it writes to the expected location
        service.Save(new AppSettings { LastPort = "test", WindowX = 0, WindowY = 0, WindowWidth = 800, WindowHeight = 600 });
        Assert.True(File.Exists(expected));
    }
}
