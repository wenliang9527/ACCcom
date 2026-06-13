using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class PresetManagerTests : IDisposable
{
    private readonly string _filePath = PresetManager.PresetsFile;

    public void Dispose()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [Fact]
    public async Task Save_ThenLoadAsync_RoundTrips()
    {
        // Arrange
        var manager = new PresetManager();
        var presets = new List<SerialPreset>
        {
            PresetManager.Create("COM3", 9600, 8, 1, 0, true, false),
            PresetManager.Create("COM5", 115200, 8, 1, 0, false, true)
        };

        // Act
        manager.Save(presets);
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal("COM3", loaded[0].Port);
        Assert.Equal(9600, loaded[0].BaudRate);
        Assert.True(loaded[0].Dtr);
        Assert.Equal(115200, loaded[1].BaudRate);
        Assert.True(loaded[1].Rts);
    }

    [Fact]
    public void GetConfig_ReturnsCorrectValues()
    {
        // Arrange
        var preset = PresetManager.Create("COM7", 57600, 8, 1, 2, true, true);

        // Act
        var (port, baud, dataBits, stopBits, parity, dtr, rts) = PresetManager.GetConfig(preset);

        // Assert
        Assert.Equal("COM7", port);
        Assert.Equal(57600, baud);
        Assert.Equal(8, dataBits);
        Assert.Equal(1, stopBits);
        Assert.Equal(2, parity);
        Assert.True(dtr);
        Assert.True(rts);
    }

    [Fact]
    public async Task LoadAsync_WithMissingFile_ReturnsEmptyList()
    {
        // Arrange: remove file if it exists
        if (File.Exists(_filePath)) File.Delete(_filePath);
        var manager = new PresetManager();

        // Act
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Empty(loaded);
    }
}
