using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class SessionRecorderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    private string NewTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"session_test_{Guid.NewGuid():N}.jsonl");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void StartRecording_WithExplicitPath_CreatesFile()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();

        // Act
        var result = recorder.StartRecording(path);

        // Assert
        Assert.True(result);
        Assert.True(File.Exists(path));
        Assert.True(recorder.IsRecording);
        recorder.StopRecording();
    }

    [Fact]
    public void StartRecording_WhenAlreadyRecording_ReturnsFalse()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();
        recorder.StartRecording(path);

        // Act
        var result = recorder.StartRecording(path);

        // Assert
        Assert.False(result);
        recorder.StopRecording();
    }

    [Fact]
    public void StopRecording_WhenRecording_ReturnsTrue()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();
        recorder.StartRecording(path);

        // Act
        var result = recorder.StopRecording();

        // Assert
        Assert.True(result);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void StopRecording_WhenNotRecording_ReturnsFalse()
    {
        // Arrange
        using var recorder = new SessionRecorder();

        // Act
        var result = recorder.StopRecording();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Record_WhenNotRecording_DoesNotThrow()
    {
        // Arrange
        using var recorder = new SessionRecorder();
        var entry = new LogEntry { Timestamp = DateTime.UtcNow, Direction = "RX", Text = "hello" };

        // Act & Assert
        var exception = Record.Exception(() => recorder.Record(entry));
        Assert.Null(exception);
    }

    [Fact]
    public void Record_IncrementsRecordedCount()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();
        recorder.StartRecording(path);
        var entry = new LogEntry { Timestamp = DateTime.UtcNow, Direction = "RX", Text = "data" };

        // Act
        recorder.Record(entry);
        recorder.Record(entry);

        // Assert
        Assert.Equal(2, recorder.RecordedCount);
        recorder.StopRecording();
    }

    [Fact]
    public void StartRecordStop_RoundTrip_ReadsBackEntries()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();
        var entry1 = new LogEntry
        {
            Timestamp = new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc),
            Direction = "RX",
            PortTag = "COM3",
            RawHex = "01 02 03",
            Text = "first"
        };
        var entry2 = new LogEntry
        {
            Timestamp = new DateTime(2026, 6, 12, 10, 0, 1, DateTimeKind.Utc),
            Direction = "TX",
            PortTag = "COM3",
            RawHex = "04 05 06",
            Text = "second"
        };

        // Act
        recorder.StartRecording(path);
        recorder.Record(entry1);
        recorder.Record(entry2);
        recorder.StopRecording();

        // Assert
        var replayed = recorder.ReplayFile(path);
        Assert.Equal(2, replayed.Count);
        Assert.Equal("RX", replayed[0].Direction);
        Assert.Equal("first", replayed[0].Text);
        Assert.Equal("COM3", replayed[0].PortTag);
        Assert.Equal("TX", replayed[1].Direction);
        Assert.Equal("second", replayed[1].Text);
    }

    [Fact]
    public void ReplayFile_WithNonExistentFile_ReturnsEmptyList()
    {
        // Arrange
        using var recorder = new SessionRecorder();

        // Act
        var result = recorder.ReplayFile("non_existent_file.jsonl");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void CurrentFile_ReturnsPathAfterStart()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();

        // Act
        recorder.StartRecording(path);

        // Assert
        Assert.Equal(path, recorder.CurrentFile);
        recorder.StopRecording();
    }

    [Fact]
    public void RecordedCount_ReturnsZeroBeforeRecording()
    {
        // Arrange & Act
        using var recorder = new SessionRecorder();

        // Assert
        Assert.Equal(0, recorder.RecordedCount);
    }
}
