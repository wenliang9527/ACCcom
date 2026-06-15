using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ShortcutViewModel : ObservableObject
{
    private readonly ISerialService _serial;
    private readonly ShortcutManager _shortcutManager;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;

    public ObservableCollection<ShortcutItem> ShortcutCommands { get; } = new();

    public ICommand SendShortcutCommand { get; }
    public ICommand AddShortcutCommand { get; }
    public ICommand DeleteShortcutCommand { get; }

    public ShortcutViewModel(
        ISerialService serial,
        ShortcutManager shortcutManager,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _serial = serial;
        _shortcutManager = shortcutManager;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        SendShortcutCommand = new RelayCommand(p => { if (p is ShortcutItem s) SendShortcut(s); });
        AddShortcutCommand = new RelayCommand(_ => AddShortcut());
        DeleteShortcutCommand = new RelayCommand(p => { if (p is ShortcutItem s) DeleteShortcut(s); });

        ShortcutCommands.CollectionChanged += (_, _) => SaveShortcuts();
    }

    public async Task LoadShortcutsAsync()
    {
        try
        {
            var items = await _shortcutManager.LoadAsync();
            foreach (var s in items)
                ShortcutCommands.Add(s);
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.LoadShortcutsFailed"], ex.Message)); }

        if (ShortcutCommands.Count == 0)
        {
            foreach (var s in ShortcutManager.GetDefaults())
                ShortcutCommands.Add(s);
        }
    }

    private void SaveShortcuts()
    {
        try { _shortcutManager.Save(ShortcutCommands); }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.SaveShortcutsFailed"], ex.Message)); }
    }

    private void SendShortcut(ShortcutItem item)
    {
        if (_serial.Send(item.Command, item.IsHex))
        {
            var df = _getDataFlow();
            df.TxCount++;
            df.RecordTxBytes(item.IsHex
                ? item.Command.Replace(" ", "").Length / 2
                : System.Text.Encoding.UTF8.GetByteCount(item.Command));
        }
    }

    private void AddShortcut()
    {
        var dlg = new AddShortcutDialog { Owner = System.Windows.Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
            ShortcutCommands.Add(new ShortcutItem { Name = dlg.ShortcutName, Command = dlg.ShortcutCommand, IsHex = dlg.ShortcutIsHex });
    }

    private void DeleteShortcut(ShortcutItem item) => ShortcutCommands.Remove(item);
}
