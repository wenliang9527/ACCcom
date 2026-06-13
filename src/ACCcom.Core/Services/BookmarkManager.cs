using System.Collections.ObjectModel;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class BookmarkManager
{
    /// <summary>
    /// Adds a bookmark for the given entry. Returns true if added, false if entry is null or already bookmarked.
    /// </summary>
    public bool AddBookmark(ObservableCollection<BookmarkItem> bookmarks, LogEntry selectedEntry)
    {
        if (selectedEntry == null) return false;
        if (bookmarks.Any(b => b.EntryId == selectedEntry.Id)) return false;

        var bm = new BookmarkItem
        {
            EntryId = selectedEntry.Id,
            Label = $"#{selectedEntry.Id}",
            Direction = selectedEntry.Direction,
            Timestamp = selectedEntry.Timestamp,
            Preview = (selectedEntry.Text ?? "").Length > 50 ? (selectedEntry.Text ?? "")[..50] : selectedEntry.Text ?? ""
        };
        bookmarks.Add(bm);
        return true;
    }

    /// <summary>
    /// Removes a bookmark from the collection. Returns the removed bookmark's label for status display.
    /// </summary>
    public string RemoveBookmark(ObservableCollection<BookmarkItem> bookmarks, BookmarkItem bm)
    {
        bookmarks.Remove(bm);
        return bm.Label;
    }

    /// <summary>
    /// Navigates bookmarks by direction (+1/-1). Returns the new index and the target entry (if found).
    /// </summary>
    public (int newIndex, LogEntry? entry, BookmarkItem? bookmark) NavigateBookmark(
        ObservableCollection<BookmarkItem> bookmarks,
        int currentIndex,
        int direction,
        ObservableCollection<LogEntry> rxEntries,
        ObservableCollection<LogEntry> txEntries)
    {
        if (bookmarks.Count == 0) return (currentIndex, null, null);

        int newIndex = currentIndex + direction;
        if (newIndex < 0) newIndex = bookmarks.Count - 1;
        if (newIndex >= bookmarks.Count) newIndex = 0;

        var bm = bookmarks[newIndex];
        var collection = bm.Direction == "RX" ? rxEntries : txEntries;
        var entry = collection.FirstOrDefault(e => e.Id == bm.EntryId);

        return (newIndex, entry, bm);
    }
}
