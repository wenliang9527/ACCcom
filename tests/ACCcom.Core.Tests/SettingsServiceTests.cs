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
    public void Load_WithCorruptedJson_SetsLastError()
    {
        // Arrange
        var path = GetTempSettingsPath();
        File.WriteAllText(path, "{ not valid json !!!");
        var service = new SettingsService(path);

        // Act
        service.Load();

        // Assert
        Assert.StartsWith("Settings file corrupted:", service.LastError);
    }

    [Fact]
    public void Load_AfterFailureSuccess_ClearsLastError()
    {
        // Arrange: first a corrupted file, then a valid one on the same instance
        var path = GetTempSettingsPath();
        File.WriteAllText(path, "{ not valid json !!!");
        var service = new SettingsService(path);
        service.Load();
        Assert.NotNull(service.LastError);

        // Act
        File.WriteAllText(path, """{ "LastPort": "COM9" }""");
        var settings = service.Load();

        // Assert
        Assert.Equal("COM9", settings.LastPort);
        Assert.Null(service.LastError);
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefaultsAndSetsLastError()
    {
        // Arrange: deny all sharing so File.ReadAllText throws IOException
        var path = GetTempSettingsPath();
        File.WriteAllText(path, """{ "LastPort": "COM3" }""");
        var service = new SettingsService(path);

        // Act
        AppSettings settings;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            settings = service.Load();
        }

        // Assert
        Assert.Equal("", settings.LastPort);
        Assert.StartsWith("Failed to read settings file:", service.LastError);
    }

    [Fact]
    public void Save_ToReadOnlyFile_ReturnsFalseAndSetsLastError()
    {
        // Arrange
        var path = GetTempSettingsPath();
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var service = new SettingsService(path);
        try
        {
            // Act
            var ok = service.Save(new AppSettings { LastPort = "COM3" });

            // Assert
            Assert.False(ok);
            Assert.StartsWith("Access denied to settings file:", service.LastError);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void DefaultSettingsPath_IsUnderLocalAppData()
    {
        // Arrange
        var service = new SettingsService();

        // Assert: the default instance writes to LocalApplicationData (not
        // AppContext.BaseDirectory — the legacy path before commit 62c4900).
        // We only verify the location; we deliberately do NOT save here, so the
        // test never touches a real user config directory.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, service.SettingsPath);
        Assert.EndsWith("settings.json", service.SettingsPath);
    }

    [Fact]
    public void WindowStates_Roundtrip_PreservesAllEntries()
    {
        // Arrange
        var service = new SettingsService(GetTempSettingsPath());
        var saved = new AppSettings
        {
            WindowStates = new Dictionary<string, WindowRect>
            {
                ["StatsWindow"] = new(100, 120, 420, 380),
                ["MacroWindow"] = new(-50, 40, 520, 860),
                ["ReplayWindow"] = new(10, 20, null, null) // NoResize: position only
            }
        };

        // Act
        service.Save(saved);
        var loaded = service.Load();

        // Assert
        Assert.Equal(3, loaded.WindowStates.Count);
        Assert.Equal(new WindowRect(100, 120, 420, 380), loaded.WindowStates["StatsWindow"]);
        Assert.Equal(new WindowRect(-50, 40, 520, 860), loaded.WindowStates["MacroWindow"]);
        Assert.Equal(new WindowRect(10, 20, null, null), loaded.WindowStates["ReplayWindow"]);
    }

    [Fact]
    public void WindowStates_MissingInOldSettings_LoadsEmpty()
    {
        // Arrange: legacy settings.json without the WindowStates key
        var path = GetTempSettingsPath();
        File.WriteAllText(path, """{ "LastPort": "COM3", "WindowX": 5, "WindowY": 6 }""");
        var service = new SettingsService(path);

        // Act
        var settings = service.Load();

        // Assert: missing dictionary defaults to empty, other fields intact
        Assert.Empty(settings.WindowStates);
        Assert.Equal("COM3", settings.LastPort);
        Assert.Equal(5, settings.WindowX);
    }
}
