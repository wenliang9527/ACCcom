using ACCcom.Core.Models;
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

    // ── Hot-reload (watcher + debounce) ──

    [Fact]
    public async Task HotReload_ChangedFile_ReloadsActiveParser()
    {
        // Arrange: activate a parser, then rewrite its file.
        var script = "return new List<FieldAnnotation>();";
        var parserPath = Path.Combine(_tempDir, "hot.csx");
        File.WriteAllText(parserPath, script);
        using var manager = new ParserManager(_tempDir);
        Assert.True(manager.Activate("hot"));

        var reloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnParserReloaded += name => reloaded.TrySetResult(name);

        // Act: overwrite the active parser file; the watcher + 500ms debounce
        // should reload it.
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");

        // Assert: reload fires with the active parser name.
        var name = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("hot", name);
    }

    [Fact]
    public void HotReload_DeletedActiveParser_Deactivates()
    {
        // Arrange
        var parserPath = Path.Combine(_tempDir, "gone.csx");
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");
        using var manager = new ParserManager(_tempDir);
        Assert.True(manager.Activate("gone"));

        // Act: delete the file while it's active.
        File.Delete(parserPath);

        // Assert: after the debounce, the parser is deactivated.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (manager.ActiveParserName != null && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
        Assert.Null(manager.ActiveParserName);
    }

    [Fact]
    public void HotReload_NewFile_AppearsInAvailableParsers()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);
        Assert.DoesNotContain("late.csx", manager.AvailableParsers);

        // Act: create a new parser file; the watcher should pick it up.
        File.WriteAllText(Path.Combine(_tempDir, "late.csx"), "return new List<FieldAnnotation>();");

        // Assert: after the debounce, the new parser is listed.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!manager.AvailableParsers.Contains("late") && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
        Assert.Contains("late", manager.AvailableParsers);
    }

    [Fact]
    public void HotReload_Disabled_DoesNotReload()
    {
        // Arrange
        var parserPath = Path.Combine(_tempDir, "static.csx");
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");
        using var manager = new ParserManager(_tempDir)
        {
            HotReloadEnabled = false
        };
        Assert.True(manager.Activate("static"));

        var reloaded = false;
        manager.OnParserReloaded += _ => reloaded = true;

        // Act: change the file; with hot reload disabled the debounce never runs.
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");

        // Give any (incorrect) reload enough time to fire, then assert none did.
        Thread.Sleep(800);
        Assert.False(reloaded);
    }

    [Fact]
    public void HotReload_MultipleChanges_DebouncesToSingleReload()
    {
        // Arrange
        var parserPath = Path.Combine(_tempDir, "burst.csx");
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");
        using var manager = new ParserManager(_tempDir);
        Assert.True(manager.Activate("burst"));

        int reloadCount = 0;
        manager.OnParserReloaded += _ => Interlocked.Increment(ref reloadCount);

        // Act: several rapid writes within the debounce window.
        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");
            Thread.Sleep(30);
        }

        // Wait for the single debounced reload to land.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (reloadCount == 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(50);

        Assert.True(reloadCount >= 1);
        // A burst of writes within the 500ms debounce window must not produce a
        // reload per write; allow one (possibly two due to timer granularity).
        Assert.True(reloadCount <= 2, $"Expected debounced reload, got {reloadCount}");
    }

    [Fact]
    public void GenerateParser_null_schema_returns_error()
    {
        using var manager = new ParserManager();

        var (success, error) = manager.GenerateParser(null);

        Assert.False(success);
        Assert.Contains("null", error);
    }

    private static ProtocolSchema CreateValidSchema(string name) => new()
    {
        Name = name,
        MinLength = 5,
        Frame = new FrameSchema
        {
            Header = "AA 55",
            Checksum = new ChecksumSchema { Type = "xor8" }
        },
        Fields = new List<FieldSchema>
        {
            new() { Name = "帧头", Offset = 0, Length = 2, Type = "hex", Value = "AA 55" },
            new() { Name = "命令", Offset = 2, Length = 1, Type = "uint8" }
        }
    };

    [Fact]
    public void GenerateParser_InvalidSchema_ReturnsValidationErrors()
    {
        // Arrange: empty name + no fields fails Validate on two rules.
        using var manager = new ParserManager(_tempDir);
        var schema = new ProtocolSchema { Name = "", Fields = new List<FieldSchema>() };

        // Act
        var (success, error) = manager.GenerateParser(schema);

        // Assert
        Assert.False(success);
        Assert.Contains("Name is required", error);
        Assert.Contains("At least one field is required", error);
    }

    [Fact]
    public void GenerateParser_ValidSchema_WritesCsxAndActivates()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var (success, error) = manager.GenerateParser(CreateValidSchema("gen_mgr_test"));

        // Assert: file written, list refreshed, generated code loads.
        Assert.True(success, error);
        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(_tempDir, "gen_mgr_test.csx")));
        Assert.Contains("gen_mgr_test", manager.AvailableParsers);
        Assert.True(manager.Activate("gen_mgr_test"));
        Assert.Equal("gen_mgr_test", manager.ActiveParserName);
    }

    [Fact]
    public void GenerateParser_TraversalName_ReturnsInvalidNameError()
    {
        // Arrange: passes Validate but must not escape the parser directory.
        using var manager = new ParserManager(_tempDir);
        var schema = CreateValidSchema("../evil");

        // Act
        var (success, error) = manager.GenerateParser(schema);

        // Assert
        Assert.False(success);
        Assert.Contains("Invalid parser name", error);
        Assert.False(File.Exists(Path.Combine(_tempDir, "evil.csx")));
    }

    [Fact]
    public void GenerateParserFromJson_InvalidJson_ReturnsError()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var (success, error) = manager.GenerateParserFromJson("{ nope");

        // Assert
        Assert.False(success);
        Assert.Contains("Invalid JSON schema", error);
    }

    [Fact]
    public void GenerateParserFromJson_ValidJson_WritesFile()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);
        var json = """{ "Name": "gen_json_test", "MinLength": 2, "Fields": [{ "Name": "f", "Offset": 0, "Length": 1, "Type": "uint8" }] }""";

        // Act
        var (success, error) = manager.GenerateParserFromJson(json);

        // Assert
        Assert.True(success, error);
        Assert.True(File.Exists(Path.Combine(_tempDir, "gen_json_test.csx")));
    }

    [Fact]
    public void GetSchema_MissingFile_ReturnsNull()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var schema = manager.GetSchema("nope");

        // Assert
        Assert.Null(schema);
    }

    [Fact]
    public void GetSchema_ValidFile_ReturnsSchema()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "s1.schema.json"),
            """{ "Name": "s1", "MinLength": 5, "Fields": [{ "Name": "f", "Offset": 0, "Length": 1, "Type": "uint8" }] }""");
        using var manager = new ParserManager(_tempDir);

        // Act
        var schema = manager.GetSchema("s1");

        // Assert
        Assert.NotNull(schema);
        Assert.Equal("s1", schema!.Name);
        Assert.Equal(5, schema.MinLength);
    }

    [Fact]
    public void GetSchema_CorruptedFile_ReturnsNull()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "bad.schema.json"), "{ nope");
        using var manager = new ParserManager(_tempDir);

        // Act
        var schema = manager.GetSchema("bad");

        // Assert
        Assert.Null(schema);
    }

    [Fact]
    public void GetFingerprint_NoSchema_ReturnsNull()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var fp = manager.GetFingerprint("nope");

        // Assert
        Assert.Null(fp);
    }

    [Fact]
    public void GetFingerprint_WithSchema_ReturnsFingerprint()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "fp1.schema.json"),
            """{ "Name": "fp1", "MinLength": 4, "Fields": [{ "Name": "f", "Offset": 0, "Length": 1, "Type": "uint8" }], "AutoMatch": { "Enabled": true, "Priority": 3, "HeaderPattern": "AA 55" } }""");
        using var manager = new ParserManager(_tempDir);

        // Act
        var fp = manager.GetFingerprint("fp1");

        // Assert
        Assert.NotNull(fp);
        Assert.Equal("AA55", fp!.HeaderHex);
        Assert.Equal(2, fp.HeaderLength);
        Assert.Equal(4, fp.MinLength);
        Assert.Equal(3, fp.Priority);
    }

    [Fact]
    public void TryResolveParserFile_RejectsNonPlainNames()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act & Assert: null / empty / (None) / traversal all rejected.
        Assert.False(manager.TryResolveParserFile(null, ".csx", out _));
        Assert.False(manager.TryResolveParserFile("  ", ".csx", out _));
        Assert.False(manager.TryResolveParserFile(ParserManager.NoParserName, ".csx", out _));
        Assert.False(manager.TryResolveParserFile("../evil", ".csx", out _));
        Assert.False(manager.TryResolveParserFile("sub/dir", ".csx", out _));
    }

    [Fact]
    public void TryResolveParserFile_ValidName_ResolvesUnderParserDir()
    {
        // Arrange
        using var manager = new ParserManager(_tempDir);

        // Act
        var ok = manager.TryResolveParserFile("good_name", ".csx", out var path);

        // Assert
        Assert.True(ok);
        Assert.Equal(Path.Combine(_tempDir, "good_name.csx"), path);
    }

    [Fact]
    public void LastError_ReflectsFailedActivate()
    {
        // Arrange: script that does not compile.
        File.WriteAllText(Path.Combine(_tempDir, "broken.csx"), "this is not valid C# !!!");
        using var manager = new ParserManager(_tempDir);

        // Act
        var ok = manager.Activate("broken");

        // Assert: activation fails and the engine error surfaces via LastError.
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(manager.LastError));
    }

    [Fact]
    public async Task HotReload_CustomDispatcher_IsUsedForReload()
    {
        // Arrange: activate a parser with a dispatch recorder.
        var parserPath = Path.Combine(_tempDir, "dp.csx");
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager = new ParserManager(_tempDir, dispatch: action =>
        {
            dispatched.TrySetResult();
            action();
        });
        Assert.True(manager.Activate("dp"));
        var reloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnParserReloaded += name => reloaded.TrySetResult(name);

        // Act: touch the file to trigger the debounced reload.
        File.WriteAllText(parserPath, "return new List<FieldAnnotation>();");

        // Assert: reload ran and went through the custom dispatcher.
        Assert.Equal("dp", await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
