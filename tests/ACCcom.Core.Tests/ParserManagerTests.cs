using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class ParserManagerTests : IDisposable
{
    private readonly string _tempDir;

    public ParserManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ParserManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Constructor_WithValidDir_CreatesDirectory()
    {
        // Arrange
        var dir = Path.Combine(_tempDir, "new_parsers");

        // Act
        using var manager = new ParserManager(dir);

        // Assert
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void AvailableParsers_IncludesNoParserName()
    {
        // Arrange & Act
        using var manager = new ParserManager(_tempDir);

        // Assert
        Assert.Contains(ParserManager.NoParserName, manager.AvailableParsers);
    }

    [Fact]
    public void AvailableParsers_ListsCSharpFiles()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "test_parser.csx"), "return new List<FieldAnnotation>();");

        // Act
        using var manager = new ParserManager(_tempDir);

        // Assert
        Assert.Contains("test_parser", manager.AvailableParsers);
    }

    [Fact]
    public void Engine_IsNotNullAfterConstruction()
    {
        // Arrange & Act
        using var manager = new ParserManager(_tempDir);

        // Assert
        Assert.NotNull(manager.Engine);
    }

    [Fact]
    public void Activate_WithValidParserName_SetsActiveParser()
    {
        // Arrange
        var script = @"
var result = new List<FieldAnnotation>();
result.Add(new FieldAnnotation {
    Name = ""Test"", Offset = 0, Length = 1,
    RawHex = ""AA"",
    DisplayValue = ""0xAA"",
    Severity = FieldSeverity.Normal
});
return result;
";
        File.WriteAllText(Path.Combine(_tempDir, "my_parser.csx"), script);
        using var manager = new ParserManager(_tempDir);

        // Act
        var result = manager.Activate("my_parser");

        // Assert
        Assert.True(result);
        Assert.Equal("my_parser", manager.ActiveParserName);
    }

    [Fact]
    public void Activate_WithNoParserName_Deactivates()
    {
        // Arrange
        var script = "return new List<FieldAnnotation>();";
        File.WriteAllText(Path.Combine(_tempDir, "active.csx"), script);
        using var manager = new ParserManager(_tempDir);
        manager.Activate("active");

        // Act
        var result = manager.Activate(ParserManager.NoParserName);

        // Assert
        Assert.True(result);
        Assert.Null(manager.ActiveParserName);
    }

    [Fact]
    public void Activate_WithNonExistentParser_ReturnsFalse()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var result = manager.Activate("does_not_exist");

        // Assert
        Assert.False(result);
        Assert.Null(manager.ActiveParserName);
    }

    [Fact]
    public void Activate_WithNull_Deactivates()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var result = manager.Activate(null);

        // Assert
        Assert.True(result);
        Assert.Null(manager.ActiveParserName);
    }

    [Fact]
    public void Refresh_DetectsNewFiles()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);
        Assert.Single(manager.AvailableParsers); // only NoParserName

        // Act
        File.WriteAllText(Path.Combine(_tempDir, "new_one.csx"), "return new List<FieldAnnotation>();");
        manager.Refresh();

        // Assert
        Assert.Contains("new_one", manager.AvailableParsers);
    }

    [Fact]
    public void GetParserDir_ReturnsConstructorPath()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var dir = manager.GetParserDir();

        // Assert
        Assert.Equal(_tempDir, dir);
    }
}
