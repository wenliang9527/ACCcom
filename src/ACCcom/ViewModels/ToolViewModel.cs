using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ToolViewModel : ObservableObject, IDisposable
{
    private readonly SerialService _serial;
    private readonly ShortcutManager _shortcutManager;
    private readonly PresetManager _presetManager;
    private readonly MacroManager _macroManager;
    private readonly BookmarkManager _bookmarkManager;
    private readonly MultiPortService _multiPort;
    private readonly TriggerService _triggerService;
    private readonly SessionRecorder _sessionRecorder;
    private readonly Action<string> _setStatus;
    private readonly Func<bool> _getIsOpen;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Func<ConnectionViewModel> _getConnection;
    private readonly Action _openPlotWindow;
    private readonly Action _openStatsWindow;
    private ReplayWindow? _replayWindow;

    public ObservableCollection<ShortcutItem> ShortcutCommands { get; } = new();
    public ObservableCollection<SerialPreset> Presets { get; } = new();
    public ObservableCollection<MacroTemplate> Macros { get; } = new();
    public ObservableCollection<PortItemViewModel> ConnectedPorts { get; } = new();
    public ObservableCollection<TriggerRule> TriggerRules { get; } = new();
    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();

    private SerialPreset? _selectedPreset;
    public SerialPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetField(ref _selectedPreset, value) && value != null)
                ApplyPreset(value);
        }
    }

    private bool _isMacroRunning;
    public bool IsMacroRunning { get => _isMacroRunning; set => SetField(ref _isMacroRunning, value); }

    private string _macroStatus = "";
    public string MacroStatus { get => _macroStatus; set => SetField(ref _macroStatus, value); }

    private string _newPortTag = "";
    public string NewPortTag { get => _newPortTag; set => SetField(ref _newPortTag, value); }

    private string _newPortName = "";
    public string NewPortName { get => _newPortName; set => SetField(ref _newPortName, value); }

    private int _newPortBaud = 115200;
    public int NewPortBaud { get => _newPortBaud; set => SetField(ref _newPortBaud, value); }

    private int _currentBookmarkIndex = -1;
    public int CurrentBookmarkIndex { get => _currentBookmarkIndex; set => SetField(ref _currentBookmarkIndex, value); }

    private bool _isLoopSend;
    public bool IsLoopSend
    {
        get => _isLoopSend;
        set { SetField(ref _isLoopSend, value); StartLoop(); }
    }

    private int _loopInterval = 1000;
    public int LoopInterval { get => _loopInterval; set => SetField(ref _loopInterval, value); }

    private bool _isLooping;
    public bool IsLooping { get => _isLooping; set => SetField(ref _isLooping, value); }

    private System.Timers.Timer? _loopTimer;

    private StatsViewModel? _statsViewModel;
    public StatsViewModel? StatsViewModel => _statsViewModel;

    public ICommand SendShortcutCommand { get; }
    public ICommand AddShortcutCommand { get; }
    public ICommand DeleteShortcutCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand RunMacroCommand { get; }
    public ICommand StopMacroCommand { get; }
    public ICommand SaveMacroCommand { get; }
    public ICommand LoadMacroCommand { get; }
    public ICommand OpenMultiPortCommand { get; }
    public ICommand CloseMultiPortCommand { get; }
    public ICommand CloseAllPortsCommand { get; }
    public ICommand SaveTriggersCommand { get; }
    public ICommand LoadTriggersCommand { get; }
    public ICommand AddTriggerCommand { get; }
    public ICommand DeleteTriggerCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand RemoveBookmarkCommand { get; }
    public ICommand NextBookmarkCommand { get; }
    public ICommand PrevBookmarkCommand { get; }
    public ICommand ReplayFileCommand { get; }
    public ICommand StopLoopCommand { get; }
    public ICommand OpenPlotCommand { get; }
    public ICommand OpenStatsCommand { get; }

    public ToolViewModel(
        SerialService serial,
        ShortcutManager shortcutManager,
        PresetManager presetManager,
        MacroManager macroManager,
        BookmarkManager bookmarkManager,
        MultiPortService multiPort,
        TriggerService triggerService,
        SessionRecorder sessionRecorder,
        Action<string> setStatus,
        Func<bool> getIsOpen,
        Func<DataFlowViewModel> getDataFlow,
        Func<ConnectionViewModel> getConnection,
        Action openPlotWindow,
        Action openStatsWindow)
    {
        _serial = serial;
        _shortcutManager = shortcutManager;
        _presetManager = presetManager;
        _macroManager = macroManager;
        _bookmarkManager = bookmarkManager;
        _multiPort = multiPort;
        _triggerService = triggerService;
        _sessionRecorder = sessionRecorder;
        _setStatus = setStatus;
        _getIsOpen = getIsOpen;
        _getDataFlow = getDataFlow;
        _getConnection = getConnection;
        _openPlotWindow = openPlotWindow;
        _openStatsWindow = openStatsWindow;

        SendShortcutCommand = new RelayCommand(p => { if (p is ShortcutItem s) SendShortcut(s); });
        AddShortcutCommand = new RelayCommand(_ => AddShortcut());
        DeleteShortcutCommand = new RelayCommand(p => { if (p is ShortcutItem s) DeleteShortcut(s); });
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        DeletePresetCommand = new RelayCommand(p => { if (p is SerialPreset s) DeletePreset(s); });
        RunMacroCommand = new RelayCommand(_ => RunMacro(), _ => !IsMacroRunning && _getIsOpen());
        StopMacroCommand = new RelayCommand(_ => StopMacro(), _ => IsMacroRunning);
        SaveMacroCommand = new RelayCommand(_ => SaveMacro());
        LoadMacroCommand = new RelayCommand(_ => LoadMacro());
        OpenMultiPortCommand = new RelayCommand(_ => OpenMultiPort(), _ => !string.IsNullOrEmpty(NewPortTag) && !string.IsNullOrEmpty(NewPortName));
        CloseMultiPortCommand = new RelayCommand(p => { if (p is PortItemViewModel item) CloseMultiPort(item); });
        CloseAllPortsCommand = new RelayCommand(_ => { _multiPort.CloseAll(); ConnectedPorts.Clear(); _setStatus("All auxiliary ports closed"); });
        SaveTriggersCommand = new RelayCommand(_ => SaveTriggers());
        LoadTriggersCommand = new RelayCommand(_ => LoadTriggers());
        AddTriggerCommand = new RelayCommand(_ => AddTrigger());
        DeleteTriggerCommand = new RelayCommand(p => { if (p is TriggerRule r) DeleteTrigger(r); });
        AddBookmarkCommand = new RelayCommand(_ => AddBookmark(), _ => _getDataFlow().SelectedEntry != null);
        RemoveBookmarkCommand = new RelayCommand(p => { if (p is BookmarkItem b) RemoveBookmark(b); });
        NextBookmarkCommand = new RelayCommand(_ => NavigateBookmark(1));
        PrevBookmarkCommand = new RelayCommand(_ => NavigateBookmark(-1));
        ReplayFileCommand = new RelayCommand(_ => ReplayFile());
        StopLoopCommand = new RelayCommand(_ => StopLoop());
        OpenPlotCommand = new RelayCommand(_ => _openPlotWindow());
        OpenStatsCommand = new RelayCommand(_ => OpenStatsWindow());

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
        catch (Exception ex) { _setStatus($"Failed to load shortcuts: {ex.Message}"); }

        if (ShortcutCommands.Count == 0)
        {
            foreach (var s in ShortcutManager.GetDefaults())
                ShortcutCommands.Add(s);
        }
    }

    private void SaveShortcuts()
    {
        try { _shortcutManager.Save(ShortcutCommands); }
        catch (Exception ex) { _setStatus($"Failed to save shortcuts: {ex.Message}"); }
    }

    public async Task LoadPresetsAsync()
    {
        try
        {
            var items = await _presetManager.LoadAsync();
            foreach (var p in items) Presets.Add(p);
        }
        catch (Exception ex) { _setStatus($"Failed to load presets: {ex.Message}"); }
    }

    private void SavePresetsToFile()
    {
        try { _presetManager.Save(Presets); }
        catch (Exception ex) { _setStatus($"Failed to save presets: {ex.Message}"); }
    }

    public async Task LoadMacrosAsync()
    {
        try
        {
            var items = await _macroManager.LoadAsync();
            foreach (var m in items) Macros.Add(m);
        }
        catch (Exception ex) { _setStatus($"Failed to load macros: {ex.Message}"); }
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

    private void ApplyPreset(SerialPreset p)
    {
        var conn = _getConnection();
        conn.SelectedPort = p.Port;
        conn.SelectedBaudRate = p.BaudRate;
        conn.SelectedDataBits = p.DataBits;
        conn.SelectedStopBits = p.StopBits;
        conn.SelectedParity = p.Parity;
        conn.DtrEnable = p.Dtr;
        conn.RtsEnable = p.Rts;
        _setStatus($"Preset loaded: {p.Name}");
    }

    private void SavePreset()
    {
        var conn = _getConnection();
        if (string.IsNullOrEmpty(conn.SelectedPort)) { _setStatus("Please select a port first"); return; }
        var preset = new SerialPreset
        {
            Name = $"{conn.SelectedPort}@{conn.SelectedBaudRate}",
            Port = conn.SelectedPort,
            BaudRate = conn.SelectedBaudRate,
            DataBits = conn.SelectedDataBits,
            StopBits = conn.SelectedStopBits,
            Parity = conn.SelectedParity,
            Dtr = conn.DtrEnable,
            Rts = conn.RtsEnable
        };
        Presets.Add(preset);
        SavePresetsToFile();
        _setStatus($"Preset saved: {preset.Name}");
    }

    private void DeletePreset(SerialPreset preset)
    {
        Presets.Remove(preset);
        SavePresetsToFile();
        _setStatus($"Preset deleted: {preset.Name}");
    }

    private void StartLoop()
    {
        var df = _getDataFlow();
        if (!IsLoopSend || string.IsNullOrEmpty(df.SendText)) return;
        StopLoop();
        _loopTimer = new System.Timers.Timer(LoopInterval > 0 ? LoopInterval : 1000);
        _loopTimer.Elapsed += (_, _) =>
        {
            if (_getIsOpen()) _serial.Send(df.SendText, df.IsHexSend);
        };
        _loopTimer.Start();
        IsLooping = true;
        _setStatus("Loop sending...");
    }

    private void StopLoop()
    {
        _loopTimer?.Stop();
        _loopTimer?.Dispose();
        _loopTimer = null;
        IsLooping = false;
        IsLoopSend = false;
        _setStatus("Loop stopped");
    }

    private void OpenMultiPort()
    {
        if (ConnectedPorts.Any(p => p.Tag == NewPortTag))
        {
            _setStatus($"Port tag '{NewPortTag}' already exists");
            return;
        }
        var config = new SerialConfig
        {
            PortName = NewPortName,
            BaudRate = NewPortBaud,
            DataBits = 8,
            StopBits = 1,
            Parity = 0
        };
        if (_multiPort.OpenPort(NewPortTag, config))
        {
            ConnectedPorts.Add(new PortItemViewModel
            {
                Tag = NewPortTag,
                PortName = NewPortName,
                BaudRate = NewPortBaud,
                IsOpen = true
            });
            _setStatus($"Opened auxiliary port: {NewPortTag} ({NewPortName})");
            NewPortTag = "";
        }
        else
        {
            _setStatus($"Failed to open auxiliary port: {NewPortName}");
        }
    }

    private void CloseMultiPort(PortItemViewModel item)
    {
        if (_multiPort.ClosePort(item.Tag))
        {
            ConnectedPorts.Remove(item);
            _setStatus($"Closed auxiliary port: {item.Tag}");
        }
    }

    public void LoadTriggers()
    {
        try
        {
            var rules = TriggerService.LoadRules("triggers.json");
            foreach (var r in rules)
            {
                _triggerService.AddRule(r);
                TriggerRules.Add(r);
            }
        }
        catch (Exception ex) { _setStatus($"Failed to load triggers: {ex.Message}"); }
    }

    private void SaveTriggers()
    {
        try
        {
            TriggerService.SaveRules(TriggerRules, "triggers.json");
            _setStatus($"Saved {TriggerRules.Count} trigger rule(s)");
        }
        catch (Exception ex) { _setStatus($"Failed to save triggers: {ex.Message}"); }
    }

    private void AddTrigger()
    {
        var rule = new TriggerRule
        {
            Name = $"Rule_{TriggerRules.Count + 1}",
            Pattern = "",
            MatchMode = "contains",
            Action = TriggerAction.None
        };
        _triggerService.AddRule(rule);
        TriggerRules.Add(rule);
    }

    private void DeleteTrigger(TriggerRule rule)
    {
        _triggerService.RemoveRule(rule.Name);
        TriggerRules.Remove(rule);
    }

    public void OnTriggerFired(TriggerRule rule, LogEntry entry)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (rule.Action)
            {
                case TriggerAction.SendCommand:
                    if (!string.IsNullOrEmpty(rule.ActionParameter))
                    {
                        _serial.Send(rule.ActionParameter, false);
                        var df = _getDataFlow();
                        df.TxCount++;
                        df.RecordTxBytes(System.Text.Encoding.UTF8.GetByteCount(rule.ActionParameter));
                    }
                    break;
                case TriggerAction.SaveToFile:
                    if (!string.IsNullOrEmpty(rule.ActionParameter))
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(rule.ActionParameter);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            var line = $"[{entry.Timestamp:HH:mm:ss.fff}] {entry.Direction} {entry.Text}";
                            File.AppendAllText(rule.ActionParameter, line + Environment.NewLine);
                        }
                        catch (Exception ex) { _setStatus($"Failed to save trigger file: {ex.Message}"); }
                    }
                    break;
                case TriggerAction.LogMessage:
                    _setStatus($"Trigger [{rule.Name}] fired: {rule.ActionParameter ?? rule.Pattern}");
                    break;
                case TriggerAction.PlaySound:
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
            }
        });
    }

    private void AddBookmark()
    {
        var df = _getDataFlow();
        if (df.SelectedEntry == null) return;
        if (_bookmarkManager.AddBookmark(Bookmarks, df.SelectedEntry))
            _setStatus($"Bookmark added: #{df.SelectedEntry.Id}");
    }

    private void RemoveBookmark(BookmarkItem bm)
    {
        var label = _bookmarkManager.RemoveBookmark(Bookmarks, bm);
        _setStatus($"Bookmark removed: {label}");
    }

    private void NavigateBookmark(int direction)
    {
        var df = _getDataFlow();
        var (newIndex, entry, bookmark) = _bookmarkManager.NavigateBookmark(
            Bookmarks, CurrentBookmarkIndex, direction, df.RxEntries, df.TxEntries);
        if (bookmark == null) return;
        CurrentBookmarkIndex = newIndex;
        if (entry != null) df.SelectedEntry = entry;
        _setStatus($"Bookmark: {bookmark.Label} ({bookmark.Direction})");
    }

    private void SaveMacro()
    {
        try
        {
            _macroManager.Save(Macros);
            _setStatus($"Saved {Macros.Count} macro(s)");
        }
        catch (Exception ex) { _setStatus($"Failed to save macros: {ex.Message}"); }
    }

    private void LoadMacro()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Macro files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var items = _macroManager.LoadFromFile(dialog.FileName);
            foreach (var m in items) Macros.Add(m);
            _setStatus($"Imported {items.Length} macro(s)");
        }
        catch (Exception ex) { _setStatus($"Failed to import macros: {ex.Message}"); }
    }

    private async void RunMacro()
    {
        if (Macros.Count == 0) { _setStatus("No macros available"); return; }
        var macro = Macros[0];
        IsMacroRunning = true;
        MacroStatus = $"Running macro: {macro.Name}";

        try
        {
            var df = _getDataFlow();
            var completed = await _macroManager.RunAsync(
                macro,
                send: (cmd, isHex) => _serial.Send(cmd, isHex),
                expandVariables: df.ExpandVariables,
                updateStatus: status => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => MacroStatus = status));

            _setStatus(completed ? "Macro completed" : "Macro stopped");
        }
        catch (Exception ex) { _setStatus($"Macro execution error: {ex.Message}"); }
        finally
        {
            IsMacroRunning = false;
            MacroStatus = "";
        }
    }

    private void StopMacro() => _macroManager.Stop();

    private void ReplayFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSONL recordings (*.jsonl)|*.jsonl|Text logs (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        _replayWindow?.Close();

        var df = _getDataFlow();
        void OnReplayEntry(LogEntry entry)
        {
            entry.PortTag = "replay";
            if (entry.Direction == "RX")
            {
                df.RunParser(entry);
                df.AddRxEntry(entry, DataFlowViewModel.CountHexBytes(entry.RawHex ?? ""));
            }
            else
            {
                df.AddTxEntry(entry, DataFlowViewModel.CountHexBytes(entry.RawHex ?? ""));
            }
        }

        _replayWindow = new ReplayWindow(dialog.FileName, _sessionRecorder, OnReplayEntry);
        _replayWindow.Owner = System.Windows.Application.Current.MainWindow;
        _replayWindow.Closed += (_, _) => _replayWindow = null;
        _replayWindow.Show();
        _setStatus($"Replay window opened: {Path.GetFileName(dialog.FileName)}");
    }

    private void OpenStatsWindow()
    {
        _statsViewModel = new StatsViewModel();
        _openStatsWindow();
        _setStatus("Statistics window opened");
    }

    public void Dispose()
    {
        _replayWindow?.Close();
        _macroManager.Dispose();
        StopLoop();
    }
}
