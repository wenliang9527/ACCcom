using System;
using System.Collections.ObjectModel;
using Xunit;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.Core.Tests;

public class BookmarkManagerTests
{
    private readonly BookmarkManager _sut = new();

    private static LogEntry MakeEntry(int id, string direction = "RX", string text = "payload")
    {
        return new LogEntry
        {
            Id = id,
            Timestamp = DateTime.Now,
            Direction = direction,
            PortTag = "COM1",
            RawHex = "AA BB",
            Text = text
        };
    }

    [Fact]
    public void AddBookmark_adds_item_returns_true()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var entry = MakeEntry(1);

        // Act
        var result = _sut.AddBookmark(bookmarks, entry);

        // Assert
        Assert.True(result);
        Assert.Single(bookmarks);
        Assert.Equal(1, bookmarks[0].EntryId);
    }

    [Fact]
    public void AddBookmark_duplicate_returns_false()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var entry = MakeEntry(1);
        _sut.AddBookmark(bookmarks, entry);

        // Act
        var result = _sut.AddBookmark(bookmarks, entry);

        // Assert
        Assert.False(result);
        Assert.Single(bookmarks);
    }

    [Fact]
    public void AddBookmark_null_entry_returns_false()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();

        // Act
        var result = _sut.AddBookmark(bookmarks, null!);

        // Assert
        Assert.False(result);
        Assert.Empty(bookmarks);
    }

    [Fact]
    public void RemoveBookmark_removes_and_returns_label()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        _sut.AddBookmark(bookmarks, MakeEntry(1, text: "test payload"));
        var item = bookmarks[0];

        // Act
        var label = _sut.RemoveBookmark(bookmarks, item);

        // Assert
        Assert.NotEmpty(label);
        Assert.Empty(bookmarks);
    }

    [Fact]
    public void NavigateBookmark_forward_wraps_around()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var rxEntries = new ObservableCollection<LogEntry>();
        var txEntries = new ObservableCollection<LogEntry>();

        for (int i = 1; i <= 3; i++)
        {
            var e = MakeEntry(i);
            rxEntries.Add(e);
            _sut.AddBookmark(bookmarks, e);
        }

        // Act - forward from last index wraps to 0
        var (newIndex, foundEntry, foundBm) = _sut.NavigateBookmark(bookmarks, currentIndex: 2, direction: 1, rxEntries, txEntries);

        // Assert
        Assert.Equal(0, newIndex);
    }

    [Fact]
    public void NavigateBookmark_backward_wraps_around()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var rxEntries = new ObservableCollection<LogEntry>();
        var txEntries = new ObservableCollection<LogEntry>();

        for (int i = 1; i <= 3; i++)
        {
            var e = MakeEntry(i);
            rxEntries.Add(e);
            _sut.AddBookmark(bookmarks, e);
        }

        // Act - backward from first index wraps to last
        var (newIndex, foundEntry, foundBm) = _sut.NavigateBookmark(bookmarks, currentIndex: 0, direction: -1, rxEntries, txEntries);

        // Assert
        Assert.Equal(2, newIndex);
    }

    [Fact]
    public void NavigateBookmark_empty_returns_current()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var rxEntries = new ObservableCollection<LogEntry>();
        var txEntries = new ObservableCollection<LogEntry>();

        // Act
        var (newIndex, entry, bookmark) = _sut.NavigateBookmark(bookmarks, currentIndex: 0, direction: 1, rxEntries, txEntries);

        // Assert
        Assert.Equal(0, newIndex);
        Assert.Null(entry);
        Assert.Null(bookmark);
    }

    [Fact]
    public void NavigateBookmark_finds_correct_entry_in_rx_or_tx()
    {
        // Arrange
        var bookmarks = new ObservableCollection<BookmarkItem>();
        var rxEntries = new ObservableCollection<LogEntry>();
        var txEntries = new ObservableCollection<LogEntry>();

        var rxEntry = MakeEntry(1, direction: "RX");
        var txEntry = MakeEntry(2, direction: "TX");
        rxEntries.Add(rxEntry);
        txEntries.Add(txEntry);
        _sut.AddBookmark(bookmarks, rxEntry);
        _sut.AddBookmark(bookmarks, txEntry);

        // Act - navigate forward from index 0 to index 1 (TX entry)
        var (newIndex, entry, bookmark) = _sut.NavigateBookmark(bookmarks, currentIndex: 0, direction: 1, rxEntries, txEntries);

        // Assert
        Assert.Equal(1, newIndex);
        Assert.NotNull(entry);
        Assert.Equal(2, entry!.Id);
        Assert.Equal("TX", entry.Direction);
    }
}
