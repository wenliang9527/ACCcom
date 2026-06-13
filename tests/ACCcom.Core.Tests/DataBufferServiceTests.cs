using System;
using System.Threading.Tasks;
using Xunit;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class DataBufferServiceTests
{
    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "hello", string hex = "48 45 4C 4C 4F")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = DateTime.Now,
            Direction = direction,
            PortTag = "COM1",
            RawHex = hex,
            Text = text
        };
    }

    [Fact]
    public void AddEntry_then_Count_returns_1()
    {
        // Arrange
        var sut = new DataBufferService();
        var entry = MakeEntry(1);

        // Act
        sut.AddEntry(entry);

        // Assert
        Assert.Equal(1, sut.Count());
    }

    [Fact]
    public void AddEntry_then_GetEntriesSince_returns_only_newer()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));
        sut.AddEntry(MakeEntry(3));

        // Act
        var result = sut.GetEntriesSince(2);

        // Assert
        Assert.Single(result);
        Assert.Equal(3, result[0].Id);
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1));
        sut.AddEntry(MakeEntry(2));

        // Act
        sut.Clear();

        // Assert
        Assert.Equal(0, sut.Count());
    }

    [Fact]
    public void Clear_with_direction_removes_only_matching()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));

        // Act
        sut.Clear("rx");

        // Assert
        Assert.Equal(1, sut.Count());
    }

    [Fact]
    public void CountWhere_filters_correctly()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "RX"));
        sut.AddEntry(MakeEntry(2, direction: "TX"));
        sut.AddEntry(MakeEntry(3, direction: "RX"));

        // Act
        var count = sut.CountWhere(e => e.Direction == "TX");

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task WaitForMatchAsync_returns_matching_entry()
    {
        // Arrange
        var sut = new DataBufferService();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            sut.AddEntry(MakeEntry(1, text: "hello world"));
        });

        // Act
        var result = await sut.WaitForMatchAsync("hello", timeoutMs: 500);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_returns_null_on_timeout()
    {
        // Arrange
        var sut = new DataBufferService();

        // Act
        var result = await sut.WaitForMatchAsync("no_match", timeoutMs: 100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_with_direction_filter()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, direction: "TX", text: "hello"));

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            sut.AddEntry(MakeEntry(2, direction: "RX", text: "hello"));
        });

        // Act
        var result = await sut.WaitForMatchAsync("hello", direction: "RX", timeoutMs: 500);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_exact_mode()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "hello world"));

        // Act - "hello" does not exact-match "hello world"
        var result = await sut.WaitForMatchAsync("hello", matchMode: "exact", timeoutMs: 100);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForMatchAsync_regex_mode()
    {
        // Arrange
        var sut = new DataBufferService();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            sut.AddEntry(MakeEntry(1, hex: "AB 12 34 CD", text: "ignored"));
        });

        // Act
        var result = await sut.WaitForMatchAsync("^AB.*CD$", matchMode: "regex", matchHex: true, timeoutMs: 500);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task WaitForMatchAsync_checks_existing_buffer()
    {
        // Arrange
        var sut = new DataBufferService();
        sut.AddEntry(MakeEntry(1, text: "already here"));

        // Act - no delay needed, entry already in buffer
        var result = await sut.WaitForMatchAsync("already here", timeoutMs: 200);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }
}
