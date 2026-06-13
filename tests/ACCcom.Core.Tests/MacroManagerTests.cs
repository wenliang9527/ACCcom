using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class MacroManagerTests : IDisposable
{
    private readonly string _filePath = MacroManager.MacrosFile;

    public void Dispose()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [Fact]
    public async Task Save_ThenLoadAsync_RoundTrips()
    {
        // Arrange
        var manager = new MacroManager();
        var macros = new List<MacroTemplate>
        {
            new() { Name = "Test", Description = "desc", RepeatCount = 2, Steps =
                new List<MacroStep> { new() { Command = "AT", IsHex = false } } }
        };

        // Act
        manager.Save(macros);
        var loaded = await manager.LoadAsync();

        // Assert
        Assert.Single(loaded);
        Assert.Equal("Test", loaded[0].Name);
        Assert.Equal(2, loaded[0].RepeatCount);
        Assert.Single(loaded[0].Steps);
        Assert.Equal("AT", loaded[0].Steps[0].Command);
    }

    [Fact]
    public async Task RunAsync_WithEmptySteps_CompletesWithoutError()
    {
        // Arrange
        using var manager = new MacroManager();
        var macro = new MacroTemplate { Name = "Empty", Steps = new List<MacroStep>(), RepeatCount = 1 };

        // Act
        var result = await manager.RunAsync(macro, (_, _) => { }, s => s, _ => { });

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsFalse()
    {
        // Arrange
        using var manager = new MacroManager();
        var macro = new MacroTemplate
        {
            Name = "Long",
            RepeatCount = 100,
            Steps = new List<MacroStep> { new() { Command = "AT", DelayMs = 500 } }
        };

        // Act
        var task = manager.RunAsync(macro, (_, _) => { }, s => s, _ => { });
        manager.Stop();
        var result = await task;

        // Assert
        Assert.False(result);
    }
}
