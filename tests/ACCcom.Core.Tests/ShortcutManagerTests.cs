using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ShortcutManagerTests : IDisposable
{
    private readonly string _filePath = ShortcutManager.ShortcutsFile;

    public void Dispose()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [Fact]
    public void GetDefaults_ReturnsNonEmptyList()
    {
        // Arrange & Act
        var defaults = ShortcutManager.GetDefaults();

        // Assert
        Assert.NotEmpty(defaults);
    }

    [Fact]
    public async Task Save_ThenLoadAsync_RoundTrips()
    {
        // Arrange
        var manager = new ShortcutManager();
        var items = new List<ShortcutItem>
        {
            new() { Name = "Test1", Command = "AT+TEST1", IsHex = false },
            new() { Name = "Test2", Command = "AA BB", IsHex = true }
        };

        // Act
        manager.Save(items);
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Test1", loaded[0].Name);
        Assert.Equal("AT+TEST1", loaded[0].Command);
        Assert.True(loaded[1].IsHex);
    }

    [Fact]
    public async Task LoadAsync_WithMissingFile_ReturnsEmptyList()
    {
        // Arrange: remove file if it exists
        if (File.Exists(_filePath)) File.Delete(_filePath);
        var manager = new ShortcutManager();

        // Act
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Empty(loaded);
    }
}
