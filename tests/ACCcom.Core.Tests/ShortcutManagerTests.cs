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
    public async Task Save_ThenLoadAsync_RoundTripsPages()
    {
        // Arrange
        var manager = new ShortcutManager();
        var pages = new List<ShortcutPage>
        {
            new()
            {
                Name = "AT 命令",
                Commands = new System.Collections.ObjectModel.ObservableCollection<ShortcutItem>
                {
                    new() { Name = "Test1", Command = "AT+TEST1", IsHex = false },
                    new() { Name = "Test2", Command = "AA BB", IsHex = true }
                }
            },
            new() { Name = "Modbus" }
        };

        // Act
        manager.Save(pages);
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal("AT 命令", loaded[0].Name);
        Assert.Equal(2, loaded[0].Commands.Count);
        Assert.Equal("Test1", loaded[0].Commands[0].Name);
        Assert.Equal("AT+TEST1", loaded[0].Commands[0].Command);
        Assert.True(loaded[0].Commands[1].IsHex);
        Assert.Equal("Modbus", loaded[1].Name);
    }

    [Fact]
    public async Task LoadAsync_WithMissingFile_ReturnsSingleDefaultPage()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
        var manager = new ShortcutManager();

        var loaded = await manager.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal(ShortcutManager.DefaultPageName, loaded[0].Name);
        Assert.Empty(loaded[0].Commands);
    }

    [Fact]
    public void ParsePages_LegacyFlatArray_MigratesToSingleDefaultPage()
    {
        var legacyJson = """
        [
          { "Name": "Old1", "Command": "AT+OLD1", "IsHex": false },
          { "Name": "Old2", "Command": "AA BB", "IsHex": true }
        ]
        """;

        var pages = ShortcutManager.ParsePages(legacyJson);

        Assert.Single(pages);
        Assert.Equal(ShortcutManager.DefaultPageName, pages[0].Name);
        Assert.Equal(2, pages[0].Commands.Count);
        Assert.Equal("Old1", pages[0].Commands[0].Name);
        Assert.True(pages[0].Commands[1].IsHex);
    }

    [Fact]
    public void ParsePages_InvalidJson_ReturnsEmpty()
    {
        var pages = ShortcutManager.ParsePages("{ not valid json !!!");

        Assert.Empty(pages);
    }

    [Fact]
    public void ExportThenImport_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ACCCOM_shortcuts_{Guid.NewGuid():N}.json");
        try
        {
            var pages = new List<ShortcutPage>
            {
                new() { Name = "P1", Commands = new System.Collections.ObjectModel.ObservableCollection<ShortcutItem> { new() { Name = "C1", Command = "01 02", IsHex = true } } },
                new() { Name = "P2" }
            };
            ShortcutManager.ExportToFile(path, pages);

            var imported = ShortcutManager.ImportFromFile(path);

            Assert.NotNull(imported);
            Assert.Equal(2, imported!.Count);
            Assert.Equal("P1", imported[0].Name);
            Assert.True(imported[0].Commands[0].IsHex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ImportFromFile_MissingFile_ReturnsNull()
    {
        var result = ShortcutManager.ImportFromFile(@"Z:\non-existent\file.json");
        Assert.Null(result);
    }
}
