using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class BookmarkViewModel : ObservableObject
{
    private readonly BookmarkManager _bookmarkManager;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;

    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();

    private int _currentBookmarkIndex = -1;
    public int CurrentBookmarkIndex { get => _currentBookmarkIndex; set => SetField(ref _currentBookmarkIndex, value); }

    public ICommand AddBookmarkCommand { get; }
    public ICommand RemoveBookmarkCommand { get; }
    public ICommand NextBookmarkCommand { get; }
    public ICommand PrevBookmarkCommand { get; }
    public ICommand JumpToBookmarkCommand { get; }

    public BookmarkViewModel(
        BookmarkManager bookmarkManager,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _bookmarkManager = bookmarkManager;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        AddBookmarkCommand = new RelayCommand(_ => AddBookmark(), _ => _getDataFlow().SelectedEntry != null);
        RemoveBookmarkCommand = new RelayCommand(p => { if (p is BookmarkItem b) RemoveBookmark(b); });
        NextBookmarkCommand = new RelayCommand(_ => NavigateBookmark(1));
        PrevBookmarkCommand = new RelayCommand(_ => NavigateBookmark(-1));
        JumpToBookmarkCommand = new RelayCommand(p => { if (p is BookmarkItem b) JumpToBookmark(b); });
    }

    private void AddBookmark()
    {
        var df = _getDataFlow();
        if (df.SelectedEntry == null) return;
        if (_bookmarkManager.AddBookmark(Bookmarks, df.SelectedEntry))
            _setStatus(string.Format(LanguageManager.Instance["Status.BookmarkAdded"], df.SelectedEntry.Id));
    }

    private void RemoveBookmark(BookmarkItem bm)
    {
        var label = _bookmarkManager.RemoveBookmark(Bookmarks, bm);
        _setStatus(string.Format(LanguageManager.Instance["Status.BookmarkRemoved"], label));
    }

    private void NavigateBookmark(int direction)
    {
        var df = _getDataFlow();
        var (newIndex, entry, bookmark) = _bookmarkManager.NavigateBookmark(
            Bookmarks, CurrentBookmarkIndex, direction, df.RxEntries, df.TxEntries);
        if (bookmark == null) return;
        CurrentBookmarkIndex = newIndex;
        if (entry != null) df.SelectedEntry = entry;
        _setStatus(string.Format(LanguageManager.Instance["Status.BookmarkNavigated"], bookmark.Label, bookmark.Direction));
    }

    /// <summary>Selects the entry a bookmark points at (the bookmark-list menu
    /// item's click target). Unlike NavigateBookmark it goes straight to the
    /// bookmark rather than stepping from the current position.</summary>
    private void JumpToBookmark(BookmarkItem bookmark)
    {
        var df = _getDataFlow();
        var index = Bookmarks.IndexOf(bookmark);
        if (index < 0) return; // bookmark removed while the menu was open
        var (_, entry, _) = _bookmarkManager.NavigateBookmark(
            Bookmarks, index - 1, 1, df.RxEntries, df.TxEntries);
        if (entry == null) return;
        CurrentBookmarkIndex = index;
        df.SelectedEntry = entry;
        _setStatus(string.Format(LanguageManager.Instance["Status.BookmarkNavigated"], bookmark.Label, bookmark.Direction));
    }
}
