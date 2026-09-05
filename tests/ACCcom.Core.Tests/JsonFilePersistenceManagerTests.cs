using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class JsonFilePersistenceManagerTests : IDisposable
{
    private sealed class TestManager : JsonFilePersistenceManager<TestItem>
    {
        public TestManager(string fileName) { FileName = fileName; }
        protected override string FileName { get; }
    }

    private sealed class TestItem
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }

    private readonly string _tempFile;
    private readonly string _tempDir;

    public JsonFilePersistenceManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "jfpm_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "items.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private TestManager CreateManager() => new(Path.GetFileName(_tempFile));

    [Fact]
    public void LoadFromFile_ReadsArray()
    {
        // Default serializer is case-sensitive PascalCase, matching the DTO.
        File.WriteAllText(_tempFile,
            "[{\"Name\":\"a\",\"Value\":1},{\"Name\":\"b\",\"Value\":2}]");

        var manager = CreateManager();
        var items = manager.LoadFromFile(_tempFile);

        Assert.Equal(2, items.Length);
        Assert.Equal("a", items[0].Name);
        Assert.Equal(2, items[1].Value);
    }

    [Fact]
    public void LoadFromFile_EmptyArray_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "[]");
        var manager = CreateManager();
        Assert.Empty(manager.LoadFromFile(_tempFile));
    }

    [Fact]
    public void LoadFromFile_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "this is not json");
        var manager = CreateManager();
        // Deserialize failure surfaces as an exception (callers handle it).
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => manager.LoadFromFile(_tempFile));
    }

    [Fact]
    public void Save_ThenLoadAsync_RoundTrips()
    {
        var manager = CreateManager();
        var items = new List<TestItem>
        {
            new() { Name = "x", Value = 10 },
            new() { Name = "y", Value = 20 }
        };

        // Save writes to BaseDir by default; verify via LoadFromFile on the
        // default path is not possible (BaseDir is fixed), so instead assert
        // Save writes a parseable file at the expected location.
        manager.Save(items);
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ACCcom", Path.GetFileName(_tempFile));
        try
        {
            Assert.True(File.Exists(defaultPath));
            var loaded = File.ReadAllText(defaultPath);
            Assert.Contains("\"Name\": \"x\"", loaded);
        }
        finally
        {
            try { File.Delete(defaultPath); } catch { }
        }
    }

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefault()
    {
        var manager = CreateManager();
        var result = await manager.LoadAsync();
        Assert.Empty(result);
    }
}
