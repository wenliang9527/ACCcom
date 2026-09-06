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
    public void Record_null_while_recording_is_noop()
    {
        // Arrange
        var path = NewTempFile();
        using var recorder = new SessionRecorder();
        recorder.StartRecording(path);
        var entry = new LogEntry { Timestamp = DateTime.UtcNow, Direction = "RX", Text = "data" };

        // Act — a null entry must not be enqueued (it would NRE in the drain
        // loop and silently lose the record).
        recorder.Record(null);
        recorder.Record(entry);

        // Assert — only the real entry counted; nothing was written for null.
        Assert.Equal(1, recorder.RecordedCount);
        recorder.StopRecording();
    }

    [Fact]
    public void Record_null_while_not_recording_does_not_throw()
    {
        using var recorder = new SessionRecorder();

        var exception = Record.Exception(() => recorder.Record(null));

        Assert.Null(exception);
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

    [Fact]
    public void RecordingsDirectory_IsUnderLocalAppData()
    {
        // Sanity check: the shared constant must stay under LocalAppData so
        // recordings survive reinstalls and don't scatter across the disk.
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SessionRecorder.RecordingsDirectory);
        Assert.EndsWith(Path.Combine("ACCcom", "recordings"), SessionRecorder.RecordingsDirectory);
    }

    [Fact]
    public void StartRecording_WithoutPath_DefaultsToRecordingsDirectory()
    {
        // Arrange
        using var recorder = new SessionRecorder();
        string? recordedFile = null;

        // Act
        try
        {
            recorder.StartRecording();
            recordedFile = recorder.CurrentFile;

            // Assert
            Assert.NotNull(recordedFile);
            Assert.Equal(
                SessionRecorder.RecordingsDirectory,
                Path.GetDirectoryName(recordedFile));
        }
        finally
        {
            recorder.StopRecording();
            // Clean up the auto-created file so the test is hermetic.
            if (recordedFile is { } f && File.Exists(f))
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    // ── Replay session (async playback: progress / cancellation / pause) ──

    /// <summary>Writes a recording file with entries spaced one second apart
    /// (timestamps drive replay pacing).</summary>
    private string WriteRecording(params string[] texts)
    {
        var path = NewTempFile();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using var sw = new StreamWriter(path);
        for (int i = 0; i < texts.Length; i++)
        {
            var ts = baseTime.AddSeconds(i).ToString("o");
            sw.WriteLine($"{{\"timestamp\":\"{ts}\",\"direction\":\"RX\",\"portTag\":\"t\",\"rawHex\":\"\",\"text\":\"{texts[i]}\"}}");
        }
        return path;
    }

    [Fact]
    public async Task ReplaySession_ReportsProgress_InOrder()
    {
        using var recorder = new SessionRecorder();
        var path = WriteRecording("a", "b", "c");

        var seen = new List<string>();
        var progress = new List<int>();
        await recorder.ReplaySessionAsync(path,
            e => seen.Add(e.Text),
            (done, total) => progress.Add(done),
            speedMultiplier: 1000);

        Assert.Equal(["a", "b", "c"], seen);
        Assert.Equal([1, 2, 3], progress);
    }

    [Fact]
    public async Task ReplaySession_Cancellation_StopsEarly()
    {
        using var recorder = new SessionRecorder();
        var path = WriteRecording("a", "b", "c", "d", "e");
        using var cts = new CancellationTokenSource();

        var seen = new List<string>();
        // Cancel after the first entry; the loop must stop before finishing.
        cts.CancelAfter(50);
        try
        {
            await recorder.ReplaySessionAsync(path, e => seen.Add(e.Text),
                speedMultiplier: 0.0001, ct: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation surfaces as an exception; that is expected.
        }

        Assert.True(seen.Count < 5, $"expected early stop, saw {seen.Count}");
    }

    [Fact]
    public async Task ReplaySession_Pause_HoldsUntilResumed()
    {
        using var recorder = new SessionRecorder();
        // One entry per second; at 1x speed the second entry is 1s later.
        var path = WriteRecording("first", "second");
        recorder.IsPaused = true;

        var seen = new List<string>();
        var replayed = recorder.ReplaySessionAsync(path, e => seen.Add(e.Text), speedMultiplier: 1);

        // Give the first entry a moment to surface, then release the pause.
        await Task.Delay(100);
        recorder.IsPaused = false;
        await replayed.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void ReplayFile_IgnoresCorruptLines()
    {
        using var recorder = new SessionRecorder();
        var path = NewTempFile();
        File.WriteAllText(path,
            "{\"text\":\"good\"}\n" +
            "this is not json\n" +
            "{\"text\":\"also-good\"}\n");

        var entries = recorder.ReplayFile(path);

        Assert.Equal(2, entries.Count);
        Assert.Equal("good", entries[0].Text);
        Assert.Equal("also-good", entries[1].Text);
    }
}
