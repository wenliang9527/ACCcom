using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class BufferedFileWriterTests : IDisposable
{
    private readonly string _testDir;

    public BufferedFileWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"buffered_writer_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void WriteCore_WritesToFile()
    {
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new TestBufferedFileWriter();
        writer.OpenWriter(filePath);

        writer.Write("Hello, World!");

        writer.CloseWriter();
        Assert.True(File.Exists(filePath));
        var content = File.ReadAllText(filePath);
        Assert.Contains("Hello, World!", content);
    }

    [Fact]
    public void WriteCore_MultipleWrites_FlushesAfter100()
    {
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new TestBufferedFileWriter();
        writer.OpenWriter(filePath);

        for (int i = 0; i < 150; i++)
            writer.Write($"Line {i}");

        writer.CloseWriter();
        var lines = File.ReadAllLines(filePath);
        Assert.Equal(150, lines.Length);
    }

    [Fact]
    public void Dispose_FlushesPendingWrites()
    {
        var filePath = Path.Combine(_testDir, "test.txt");
        var writer = new TestBufferedFileWriter();
        writer.OpenWriter(filePath);

        writer.Write("Test line");

        writer.Dispose();
        var content = File.ReadAllText(filePath);
        Assert.Contains("Test line", content);
    }

    [Fact]
    public void OpenWriter_ChangesFile()
    {
        var file1 = Path.Combine(_testDir, "test1.txt");
        var file2 = Path.Combine(_testDir, "test2.txt");
        var writer = new TestBufferedFileWriter();

        writer.OpenWriter(file1);
        writer.Write("File 1");

        writer.OpenWriter(file2);
        writer.Write("File 2");

        writer.CloseWriter();

        Assert.Contains("File 1", File.ReadAllText(file1));
        Assert.Contains("File 2", File.ReadAllText(file2));
    }

    private class TestBufferedFileWriter : BufferedFileWriter
    {
        public new void OpenWriter(string filePath, bool append = true)
            => base.OpenWriter(filePath, append);

        public void Write(string line)
            => WriteCore(line);

        public new void CloseWriter()
            => base.CloseWriter();
    }
}
