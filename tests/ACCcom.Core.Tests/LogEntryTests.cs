using ACCcom.Core.Models;
using Xunit;

namespace ACCcom.Core.Tests;

public class LogEntryTests
{
    [Fact]
    public void Default_PortTag_IsEmpty()
    {
        // Entries arrive from the default single-port session with no tag; the
        // UI normalizes empty tags to MainPortTag at the display layer.
        var entry = new LogEntry();
        Assert.Equal("", entry.PortTag);
    }

    [Fact]
    public void MainPortTag_Is_StableConvention()
    {
        // The display-level default tag is a shared convention, not a magic
        // string scattered across UI code paths.
        Assert.Equal("main", LogEntry.MainPortTag);
    }

    [Fact]
    public void NotifyHighlightChanged_raises_PropertyChanged_for_HighlightColor()
    {
        // The HighlightColor setter is a plain auto-property on the hot path;
        // batch recomputes (rule edits, theme switches) rely on this explicit
        // notification so the Foreground binding re-runs its converter.
        var entry = new LogEntry { HighlightColor = "#FF6B6B" };
        string? raised = null;
        entry.PropertyChanged += (_, e) => raised = e.PropertyName;

        entry.NotifyHighlightChanged();

        Assert.Equal(nameof(LogEntry.HighlightColor), raised);
    }
}
