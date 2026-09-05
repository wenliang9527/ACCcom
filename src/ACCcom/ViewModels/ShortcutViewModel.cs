using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ShortcutViewModel : ObservableObject
{
    private readonly ISerialService _serial;
    private readonly NetworkBridgeService? _networkBridge;
    private readonly ShortcutManager _shortcutManager;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;
    private bool _loading;
    private ShortcutPage? _currentPage;

    public ObservableCollection<ShortcutPage> Pages { get; } = new();

    public ShortcutPage? CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetField(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentCommands));
                OnPropertyChanged(nameof(PageIndicator));
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
                RebuildVisibleCommands();
            }
        }
    }

    public ObservableCollection<ShortcutItem>? CurrentCommands => CurrentPage?.Commands;

    private string _filterText = "";
    /// <summary>Search filter applied to command names (case-insensitive substring). Empty = show all.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? ""))
            {
                RebuildVisibleCommands();
            }
        }
    }

    /// <summary>Filtered view of <see cref="CurrentCommands"/>. Rebuilt when filter or page changes.</summary>
    public ObservableCollection<ShortcutItem> VisibleCommands { get; } = new();

    private void RebuildVisibleCommands()
    {
        VisibleCommands.Clear();
        var source = CurrentCommands;
        if (source == null) return;
        if (string.IsNullOrEmpty(_filterText))
        {
            foreach (var cmd in source) VisibleCommands.Add(cmd);
            return;
        }
        foreach (var cmd in source)
        {
            if (cmd.Name != null && cmd.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                VisibleCommands.Add(cmd);
        }
    }

    public string PageIndicator
    {
        get
        {
            if (Pages.Count == 0 || CurrentPage == null) return "0/0";
            return $"{Pages.IndexOf(CurrentPage) + 1}/{Pages.Count}";
        }
    }

    public bool CanGoPrev => Pages.Count > 1;
    public bool CanGoNext => Pages.Count > 1;

    public ICommand SendShortcutCommand { get; }
    public ICommand AddShortcutCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand DeletePageCommand { get; }
    public ICommand ExportAllCommand { get; }
    public ICommand ExportCurrentPageCommand { get; }
    public ICommand ImportCommand { get; }

    public ShortcutViewModel(
        ISerialService serial,
        ShortcutManager shortcutManager,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus,
        NetworkBridgeService? networkBridge = null)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _shortcutManager = shortcutManager;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        SendShortcutCommand = new RelayCommand(p => { if (p is ShortcutItem s) SendShortcut(s); });
        AddShortcutCommand = new RelayCommand(_ => AddShortcut());
        PrevPageCommand = new RelayCommand(_ => GoToPage(-1));
        NextPageCommand = new RelayCommand(_ => GoToPage(1));
        AddPageCommand = new RelayCommand(_ => AddPage());
        DeletePageCommand = new RelayCommand(_ => DeleteCurrentPage());
        ExportAllCommand = new RelayCommand(_ => ExportPages(Pages.ToList()));
        ExportCurrentPageCommand = new RelayCommand(_ => { if (CurrentPage != null) ExportPages(new List<ShortcutPage> { CurrentPage }); });
        ImportCommand = new RelayCommand(_ => Import());
    }

    public async Task LoadShortcutsAsync()
    {
        _loading = true;
        try
        {
            var pages = await _shortcutManager.LoadAsync();
            foreach (var page in pages)
                AttachPage(page);

            if (Pages.Count == 0)
                AttachPage(new ShortcutPage { Name = ShortcutManager.DefaultPageName });

            CurrentPage = Pages[0];
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.LoadShortcutsFailed"], ex.Message)); }
        finally
        {
            _loading = false;
        }
    }

    // ===== Item operations =====

    public void SendShortcut(ShortcutItem item)
    {
        var df = _getDataFlow();
        var toSend = item.IsHex ? item.Command : df.ExpandVariables(item.Command);

        bool sent;
        if (_networkBridge?.IsConnected == true)
            sent = _networkBridge.Send(toSend, item.IsHex);
        else
            sent = _serial.Send(toSend, item.IsHex);

        if (sent)
        {
            df.TxCount++;
            df.RecordTxBytes(item.IsHex
                ? toSend.Replace(" ", "").Length / 2
                : System.Text.Encoding.UTF8.GetByteCount(toSend));
            _setStatus(string.Format(LanguageManager.Instance["Status.ShortcutSent"], item.Name));
        }
        else
        {
            _setStatus(LanguageManager.Instance["Status.PortClosed"]);
        }
    }

    /// <summary>Sends the command at the given position on the current page (Alt+1~9 hotkey entry point).</summary>
    public void SendByIndex(int index)
    {
        if (CurrentCommands == null || index < 0 || index >= CurrentCommands.Count) return;
        SendShortcut(CurrentCommands[index]);
    }

    /// <summary>Fills the send box with the command without transmitting.</summary>
    public void LoadToSender(ShortcutItem item)
    {
        var df = _getDataFlow();
        df.SendText = item.IsHex ? item.Command : df.ExpandVariables(item.Command);
        df.IsHexSend = item.IsHex;
        _setStatus(string.Format(LanguageManager.Instance["Status.ShortcutLoaded"], item.Name));
    }

    public void AddShortcut()
    {
        if (CurrentCommands == null) return;
        var dlg = new AddShortcutDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
            CurrentCommands.Add(new ShortcutItem { Name = dlg.ShortcutName, Command = dlg.ShortcutCommand, IsHex = dlg.ShortcutIsHex });
    }

    /// <summary>Edits an existing shortcut in place (double-click entry point).</summary>
    public void EditShortcut(ShortcutItem item)
    {
        if (CurrentCommands == null) return;
        var dlg = new AddShortcutDialog(item.Name, item.Command, item.IsHex, isEdit: true)
            { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() != true) return;

        var index = CurrentCommands.IndexOf(item);
        if (index >= 0)
            CurrentCommands[index] = new ShortcutItem
                { Name = dlg.ShortcutName, Command = dlg.ShortcutCommand, IsHex = dlg.ShortcutIsHex };
    }

    public void DeleteShortcut(ShortcutItem item) => CurrentCommands?.Remove(item);

    /// <summary>Copies the command text to the clipboard. Useful when the user wants
    /// to paste the command into an external terminal or other app without first
    /// loading it into the send box.</summary>
    public void CopyCommand(ShortcutItem item)
    {
        if (item == null) return;
        try
        {
            System.Windows.Clipboard.SetText(item.Command ?? "");
            _setStatus(string.Format(LanguageManager.Instance["Status.ShortcutCopied"], item.Name));
        }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
    }

    /// <summary>Toggles the HEX/TXT mode of a single shortcut without opening the
    /// edit dialog. Lets users quickly probe "would this command work as HEX?"
    /// without going through two clicks.</summary>
    public void ToggleIsHex(ShortcutItem item)
    {
        if (item == null || CurrentCommands == null) return;
        var index = CurrentCommands.IndexOf(item);
        if (index < 0) return;
        CurrentCommands[index] = new ShortcutItem
        {
            Name = item.Name,
            Command = item.Command,
            IsHex = !item.IsHex
        };
        _setStatus(string.Format(LanguageManager.Instance["Status.ShortcutToggledHex"],
            item.Name, CurrentCommands[index].IsHex ? "HEX" : "TXT"));
    }

    // ===== Page operations =====

    private void GoToPage(int delta)
    {
        if (Pages.Count == 0 || CurrentPage == null) return;
        var idx = (Pages.IndexOf(CurrentPage) + delta + Pages.Count) % Pages.Count;
        CurrentPage = Pages[idx];
    }

    public void SelectPage(ShortcutPage page) => CurrentPage = page;

    public void AddPage()
    {
        AttachPage(new ShortcutPage { Name = NewPageName() });
        CurrentPage = Pages[^1];
        SaveShortcuts();
        _setStatus(string.Format(LanguageManager.Instance["Status.PageCreated"], CurrentPage.Name));
    }

    public void RenameCurrentPage(string? newName = null)
    {
        if (CurrentPage == null) return;

        newName ??= PromptDialog.Show(
            LanguageManager.Instance["Prompt.RenameTitle"],
            LanguageManager.Instance["QuickSend.RenamePage"],
            CurrentPage.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        CurrentPage.Name = newName.Trim();
        SaveShortcuts();

        // Refresh the page selector display.
        var page = CurrentPage;
        var idx = Pages.IndexOf(page);
        Pages[idx] = page;
        CurrentPage = page;
        _setStatus(string.Format(LanguageManager.Instance["Status.PageRenamed"], page.Name));
    }

    public void DeleteCurrentPage()
    {
        if (CurrentPage == null || Pages.Count <= 1) return;

        var confirm = string.Format(LanguageManager.Instance["Confirm.DeletePage"],
            CurrentPage.Name, CurrentPage.Commands.Count);
        var result = System.Windows.MessageBox.Show(confirm,
            LanguageManager.Instance["QuickSend.DeletePage"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        var name = CurrentPage.Name;
        DetachPage(CurrentPage);
        var removedIndex = Math.Max(0, Pages.IndexOf(CurrentPage) - 1);
        Pages.Remove(CurrentPage);
        CurrentPage = Pages[Math.Min(removedIndex, Pages.Count - 1)];
        SaveShortcuts();
        _setStatus(string.Format(LanguageManager.Instance["Status.PageDeleted"], name));
    }

    // ===== Import / Export =====

    public void ExportPages(List<ShortcutPage> pages)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"ACCCOM_shortcuts_{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            ShortcutManager.ExportToFile(dialog.FileName, pages);
            _setStatus(string.Format(LanguageManager.Instance["Status.ExportDone"], dialog.FileName));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.SaveShortcutsFailed"], ex.Message));
        }
    }

    public void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        var imported = ShortcutManager.ImportFromFile(dialog.FileName);
        if (imported == null)
        {
            _setStatus(LanguageManager.Instance["Status.ImportFailed"]);
            return;
        }

        var merge = System.Windows.MessageBox.Show(
            LanguageManager.Instance["Confirm.ImportMode"],
            LanguageManager.Instance["QuickSend.Import"],
            System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
        if (merge == System.Windows.MessageBoxResult.Cancel) return;

        _loading = true;
        try
        {
            if (merge == System.Windows.MessageBoxResult.No)
            {
                foreach (var page in Pages.ToList())
                    DetachPage(page);
                Pages.Clear();
            }

            foreach (var page in imported)
            {
                if (merge == System.Windows.MessageBoxResult.Yes)
                    page.Name = UniquePageName(page.Name);
                AttachPage(page);
            }

            CurrentPage = Pages[^1];
        }
        finally
        {
            _loading = false;
        }

        SaveShortcuts();
        _setStatus(string.Format(LanguageManager.Instance["Status.ImportDone"], imported.Count));
    }

    // ===== Persistence plumbing =====

    private void AttachPage(ShortcutPage page)
    {
        page.Commands.CollectionChanged += OnCommandsChanged;
        Pages.Add(page);
    }

    private void DetachPage(ShortcutPage page)
    {
        page.Commands.CollectionChanged -= OnCommandsChanged;
    }

    private void OnCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildVisibleCommands();
        SaveShortcuts();
    }

    private void SaveShortcuts()
    {
        if (_loading) return;
        try { _shortcutManager.Save(Pages); }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.SaveShortcutsFailed"], ex.Message)); }
    }

    private string NewPageName()
    {
        int n = Pages.Count + 1;
        while (Pages.Any(p => p.Name == string.Format(LanguageManager.Instance["QuickSend.DefaultPageName"], n))) n++;
        return string.Format(LanguageManager.Instance["QuickSend.DefaultPageName"], n);
    }

    private string UniquePageName(string baseName)
    {
        if (!Pages.Any(p => p.Name == baseName)) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!Pages.Any(p => p.Name == candidate)) return candidate;
        }
    }
}
