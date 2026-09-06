using System;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using Xunit;

namespace ACCcom.Core.Tests;

public class TriggerLogLineTests
{
    [Fact]
    public void Format_IncludesTimestampDirectionAndText()
    {
        var entry = new LogEntry
        {
            Timestamp = new DateTime(2026, 9, 6, 12, 34, 56, 789),
            Direction = "RX",
            Text = "hello"
        };

        Assert.Equal("[12:34:56.789] RX hello", TriggerLogLine.Format(entry));
    }

    [Fact]
    public void Format_EmptyText_YieldsTrailingSpace()
    {
        var entry = new LogEntry
        {
            Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, 6),
            Direction = "TX",
            Text = ""
        };

        Assert.Equal("[03:04:05.006] TX ", TriggerLogLine.Format(entry));
    }

    [Fact]
    public void Format_NullEntry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TriggerLogLine.Format(null!));
    }
}