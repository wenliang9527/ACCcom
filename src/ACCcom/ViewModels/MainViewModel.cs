using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class MainViewModel : ObservableObject, IDisposable
{
    private static readonly Regex KeyValueRegex = new(@"[=:]?\s*(-?\d+\.?\d*)", RegexOptions.Compiled);
    private static readonly Regex StandaloneNumberRegex = new(@"-?\d+\.\d+", RegexOptions.Compiled);
    private readonly SerialService _serial = new();
    private readonly NetworkBridgeService _networkBridge = new();
    private readonly LoggerService _logger = new();
    private readonly HttpService _http;
    private readonly ParserManager _parserManager;
    private readonly MultiPortService _multiPort = new();
    private readonly DataStatistics _stats = new();
    private readonly ShortcutManager _shortcutManager = new();
    private readonly PresetManager _presetManager = new();
    private readonly MacroManager _macroManager = new();
    private readonly BookmarkManager _bookmarkManager = new();
    private readonly FileExportService _fileExportService = new();
    private readonly SerialConnectionManager _connectionManager = new();
    private readonly SessionRecorder _sessionRecorder = new();
    private readonly TriggerService _triggerService = new();
    private readonly PlotViewModel _plotViewModel = new();
    private readonly SettingsService _settingsService = new();
    private AppSettings _settings;
    private PlotWindow? _plotWindow;
    private bool _disposed;

    private readonly ConnectionViewModel _connection;
    private readonly DataFlowViewModel _dataFlow;
    private readonly ToolViewModel _tool;

    public ConnectionViewModel Connection => _connection;
    public DataFlowViewModel DataFlow => _dataFlow;
    public ToolViewModel Tool => _tool;

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private bool _isDarkTheme;
    public bool IsDarkTheme { get => _isDarkTheme; set => SetField(ref _isDarkTheme, value); }

    private string _httpUrl = HttpService.DefaultUrl;
    public string HttpUrl { get => _httpUrl; set => SetField(ref _httpUrl, value); }

    public AppSettings Settings => _settings;

    public ICommand ToggleThemeCommand { get; }

    public MainViewModel()
    {
        _settings = _settingsService.Load();
        _parserManager = new ParserManager(dispatch: action => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action));

        _http = new HttpService(_serial, _parserManager);
        _http.Start();

        _connection = new ConnectionViewModel(_serial, _networkBridge, _connectionManager, msg => StatusText = msg);
        _dataFlow = new DataFlowViewModel(_serial, _networkBridge, _logger, _http, _triggerService, _parserManager, _stats, _fileExportService, msg => StatusText = msg);
        _tool = new ToolViewModel(
            _serial, _shortcutManager, _presetManager, _macroManager, _bookmarkManager,
            _multiPort, _triggerService, _sessionRecorder,
            msg => StatusText = msg,
            () => _connection.IsOpen,
            () => _dataFlow,
            () => _connection,
            () => OpenPlotWindow(),
            () => OpenStatsWindow());

        _connection.PropertyChanged += (_, e) => RaisePropertyChanged(e);
        _dataFlow.PropertyChanged += (_, e) => RaisePropertyChanged(e);
        _tool.PropertyChanged += (_, e) => RaisePropertyChanged(e);

        ToggleThemeCommand = new RelayCommand(_ =>
        {
            IsDarkTheme = !IsDarkTheme;
            App.ApplyTheme(IsDarkTheme);
        });

        _serial.OnDataReceived += _dataFlow.OnSerialData;
        _serial.OnError += msg => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        _serial.OnDisconnected += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _connection.IsOpen = false; StatusText = "Port disconnected"; });

        _networkBridge.OnDataReceived += _dataFlow.OnSerialData;
        _networkBridge.OnError += msg => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        _networkBridge.OnDisconnected += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _connection.IsOpen = false; StatusText = "Network disconnected"; });

        _triggerService.OnTriggerFired += _tool.OnTriggerFired;

        _multiPort.OnDataReceived += entry =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (entry.Direction == "RX")
                    _dataFlow.AddRxEntry(entry, 0);
                else
                    _dataFlow.AddTxEntry(entry, 0);
            });
        };

        _dataFlow.OnRxProcessed = entry =>
        {
            if (_plotWindow != null)
            {
                var values = ExtractNumericValues(entry.Text ?? "");
                foreach (var v in values)
                    _plotViewModel.AddPoint(v);
            }
        };

        _dataFlow.OnEntryProcessed = (entry, byteCount) =>
        {
            if (entry.Direction == "TX")
                _tool.StatsViewModel?.RecordTx(byteCount);
        };

        HttpUrl = HttpService.DefaultUrl;

        SelectedBaudRate = _settings.LastBaudRate;
        SelectedDataBits = _settings.LastDataBits;
        IsHexSend = _settings.IsHexSend;
        IsHexDisplayRx = _settings.IsHexDisplayRx;
        IsHexDisplayTx = _settings.IsHexDisplayTx;
        EnableRxTimestamp = _settings.EnableRxTimestamp;
        EnableTxTimestamp = _settings.EnableTxTimestamp;
        IsDarkTheme = _settings.IsDarkTheme;
        _connection.SelectedLanguage = _settings.Language;
        LanguageManager.Instance.LoadLanguage(_settings.Language);
        if (!string.IsNullOrEmpty(_settings.LastPort) && _connection.AvailablePorts.Contains(_settings.LastPort))
            _connection.SelectedPort = _settings.LastPort;

        var statsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        statsTimer.Tick += (_, _) =>
        {
            RxRate = $"{_stats.RxBytesPerSecond:F1} B/s | {_stats.RxFramesPerSecond:F1} fps";
            ErrorRate = $"{_stats.ErrorRate:F1}%";
            FrameInterval = $"{_stats.AvgFrameIntervalMs:F1} ms";
            _tool.StatsViewModel?.Update(_stats, RxByteCount, TxByteCount, RxCount, TxCount, ConnectionDuration);
        };
        statsTimer.Start();
    }

    public async Task InitializeAsync()
    {
        await _tool.LoadShortcutsAsync();
        await _tool.LoadPresetsAsync();
        await _tool.LoadMacrosAsync();
        _tool.LoadTriggers();
    }

    public void NavigateHistory(int direction) => _dataFlow.NavigateHistory(direction);

    public ObservableRangeCollection<LogEntry> RxEntries => _dataFlow.RxEntries;
    public ObservableRangeCollection<LogEntry> TxEntries => _dataFlow.TxEntries;
    public ObservableCollection<string> AvailablePorts => _connection.AvailablePorts;
    public ObservableCollection<int> BaudRates => _connection.BaudRates;
    public ObservableCollection<int> DataBitsList => _connection.DataBitsList;
    public ObservableCollection<string> StopBitsList => _connection.StopBitsList;
    public ObservableCollection<string> ParityList => _connection.ParityList;
    public string SelectedPort { get => _connection.SelectedPort; set => _connection.SelectedPort = value; }
    public int SelectedBaudRate { get => _connection.SelectedBaudRate; set => _connection.SelectedBaudRate = value; }
    public int SelectedDataBits { get => _connection.SelectedDataBits; set => _connection.SelectedDataBits = value; }
    public int SelectedStopBits { get => _connection.SelectedStopBits; set => _connection.SelectedStopBits = value; }
    public int SelectedParity { get => _connection.SelectedParity; set => _connection.SelectedParity = value; }
    public bool DtrEnable { get => _connection.DtrEnable; set => _connection.DtrEnable = value; }
    public bool RtsEnable { get => _connection.RtsEnable; set => _connection.RtsEnable = value; }
    public bool AutoReconnect { get => _connection.AutoReconnect; set => _connection.AutoReconnect = value; }
    public int ReconnectIntervalMs { get => _connection.ReconnectIntervalMs; set => _connection.ReconnectIntervalMs = value; }
    public int MaxReconnectAttempts { get => _connection.MaxReconnectAttempts; set => _connection.MaxReconnectAttempts = value; }
    public ObservableCollection<string> ConnectionTypes => _connection.ConnectionTypes;
    public ObservableCollection<string> Languages => _connection.Languages;
    public string SelectedConnectionType { get => _connection.SelectedConnectionType; set => _connection.SelectedConnectionType = value; }
    public string SelectedLanguage { get => _connection.SelectedLanguage; set => _connection.SelectedLanguage = value; }
    public string NetworkHost { get => _connection.NetworkHost; set => _connection.NetworkHost = value; }
    public int NetworkPort { get => _connection.NetworkPort; set => _connection.NetworkPort = value; }
    public bool IsOpen { get => _connection.IsOpen; set => _connection.IsOpen = value; }
    public string ConnectionDuration { get => _connection.ConnectionDuration; set => _connection.ConnectionDuration = value; }

    public string SendText { get => _dataFlow.SendText; set => _dataFlow.SendText = value; }
    public bool IsHexSend { get => _dataFlow.IsHexSend; set => _dataFlow.IsHexSend = value; }
    public bool IsHexDisplayRx { get => _dataFlow.IsHexDisplayRx; set => _dataFlow.IsHexDisplayRx = value; }
    public bool IsHexDisplayTx { get => _dataFlow.IsHexDisplayTx; set => _dataFlow.IsHexDisplayTx = value; }
    public bool EnableRxTimestamp { get => _dataFlow.EnableRxTimestamp; set => _dataFlow.EnableRxTimestamp = value; }
    public bool EnableTxTimestamp { get => _dataFlow.EnableTxTimestamp; set => _dataFlow.EnableTxTimestamp = value; }
    public int RxCount { get => _dataFlow.RxCount; set => _dataFlow.RxCount = value; }
    public int TxCount { get => _dataFlow.TxCount; set => _dataFlow.TxCount = value; }
    public int RxByteCount { get => _dataFlow.RxByteCount; set => _dataFlow.RxByteCount = value; }
    public int TxByteCount { get => _dataFlow.TxByteCount; set => _dataFlow.TxByteCount = value; }
    public int ErrorFrameCount { get => _dataFlow.ErrorFrameCount; set => _dataFlow.ErrorFrameCount = value; }
    public string RxRate { get => _dataFlow.RxRate; set => _dataFlow.RxRate = value; }
    public string ErrorRate { get => _dataFlow.ErrorRate; set => _dataFlow.ErrorRate = value; }
    public string FrameInterval { get => _dataFlow.FrameInterval; set => _dataFlow.FrameInterval = value; }
    public string RxFilterText { get => _dataFlow.RxFilterText; set => _dataFlow.RxFilterText = value; }
    public string TxFilterText { get => _dataFlow.TxFilterText; set => _dataFlow.TxFilterText = value; }
    public bool IsRegexFilter { get => _dataFlow.IsRegexFilter; set => _dataFlow.IsRegexFilter = value; }
    public bool ShowRx { get => _dataFlow.ShowRx; set => _dataFlow.ShowRx = value; }
    public bool ShowTx { get => _dataFlow.ShowTx; set => _dataFlow.ShowTx = value; }
    public ListCollectionView? FilteredRxEntries => _dataFlow.FilteredRxEntries;
    public ListCollectionView? FilteredTxEntries => _dataFlow.FilteredTxEntries;
    public bool AutoScrollRx { get => _dataFlow.AutoScrollRx; set => _dataFlow.AutoScrollRx = value; }
    public bool AutoScrollTx { get => _dataFlow.AutoScrollTx; set => _dataFlow.AutoScrollTx = value; }
    public string SelectedParser { get => _dataFlow.SelectedParser; set => _dataFlow.SelectedParser = value; }
    public ObservableCollection<string> AvailableParsers => _dataFlow.AvailableParsers;
    public LogEntry? SelectedEntry { get => _dataFlow.SelectedEntry; set => _dataFlow.SelectedEntry = value; }
    public bool HasFields => _dataFlow.HasFields;

    public ObservableCollection<ShortcutItem> ShortcutCommands => _tool.ShortcutCommands;
    public ObservableCollection<SerialPreset> Presets => _tool.Presets;
    public SerialPreset? SelectedPreset { get => _tool.SelectedPreset; set => _tool.SelectedPreset = value; }
    public ObservableCollection<MacroTemplate> Macros => _tool.Macros;
    public bool IsMacroRunning { get => _tool.IsMacroRunning; set => _tool.IsMacroRunning = value; }
    public string MacroStatus { get => _tool.MacroStatus; set => _tool.MacroStatus = value; }
    public ObservableCollection<PortItemViewModel> ConnectedPorts => _tool.ConnectedPorts;
    public string NewPortTag { get => _tool.NewPortTag; set => _tool.NewPortTag = value; }
    public string NewPortName { get => _tool.NewPortName; set => _tool.NewPortName = value; }
    public int NewPortBaud { get => _tool.NewPortBaud; set => _tool.NewPortBaud = value; }
    public ObservableCollection<TriggerRule> TriggerRules => _tool.TriggerRules;
    public ObservableCollection<BookmarkItem> Bookmarks => _tool.Bookmarks;
    public int CurrentBookmarkIndex { get => _tool.CurrentBookmarkIndex; set => _tool.CurrentBookmarkIndex = value; }
    public bool IsLoopSend { get => _tool.IsLoopSend; set => _tool.IsLoopSend = value; }
    public int LoopInterval { get => _tool.LoopInterval; set => _tool.LoopInterval = value; }
    public bool IsLooping { get => _tool.IsLooping; set => _tool.IsLooping = value; }

    public ICommand OpenCloseCommand => _connection.OpenCloseCommand;
    public ICommand ConnectNetworkCommand => _connection.ConnectNetworkCommand;
    public ICommand RefreshPortsCommand => _connection.RefreshPortsCommand;
    public ICommand SendCommand => _dataFlow.SendCommand;
    public ICommand ClearRxCommand => _dataFlow.ClearRxCommand;
    public ICommand ClearTxCommand => _dataFlow.ClearTxCommand;
    public ICommand SaveRxCommand => _dataFlow.SaveRxCommand;
    public ICommand SaveTxCommand => _dataFlow.SaveTxCommand;
    public ICommand SaveRxJsonCommand => _dataFlow.SaveRxJsonCommand;
    public ICommand SaveTxJsonCommand => _dataFlow.SaveTxJsonCommand;
    public ICommand SaveRxCsvCommand => _dataFlow.SaveRxCsvCommand;
    public ICommand SaveTxCsvCommand => _dataFlow.SaveTxCsvCommand;
    public ICommand OpenParserDirCommand => _dataFlow.OpenParserDirCommand;
    public ICommand CompareFramesCommand => _dataFlow.CompareFramesCommand;
    public ICommand SendShortcutCommand => _tool.SendShortcutCommand;
    public ICommand AddShortcutCommand => _tool.AddShortcutCommand;
    public ICommand DeleteShortcutCommand => _tool.DeleteShortcutCommand;
    public ICommand SavePresetCommand => _tool.SavePresetCommand;
    public ICommand DeletePresetCommand => _tool.DeletePresetCommand;
    public ICommand RunMacroCommand => _tool.RunMacroCommand;
    public ICommand StopMacroCommand => _tool.StopMacroCommand;
    public ICommand SaveMacroCommand => _tool.SaveMacroCommand;
    public ICommand LoadMacroCommand => _tool.LoadMacroCommand;
    public ICommand OpenMultiPortCommand => _tool.OpenMultiPortCommand;
    public ICommand CloseMultiPortCommand => _tool.CloseMultiPortCommand;
    public ICommand CloseAllPortsCommand => _tool.CloseAllPortsCommand;
    public ICommand SaveTriggersCommand => _tool.SaveTriggersCommand;
    public ICommand LoadTriggersCommand => _tool.LoadTriggersCommand;
    public ICommand AddTriggerCommand => _tool.AddTriggerCommand;
    public ICommand DeleteTriggerCommand => _tool.DeleteTriggerCommand;
    public ICommand AddBookmarkCommand => _tool.AddBookmarkCommand;
    public ICommand RemoveBookmarkCommand => _tool.RemoveBookmarkCommand;
    public ICommand NextBookmarkCommand => _tool.NextBookmarkCommand;
    public ICommand PrevBookmarkCommand => _tool.PrevBookmarkCommand;
    public ICommand ReplayFileCommand => _tool.ReplayFileCommand;
    public ICommand StopLoopCommand => _tool.StopLoopCommand;
    public ICommand OpenPlotCommand => _tool.OpenPlotCommand;
    public ICommand OpenStatsCommand => _tool.OpenStatsCommand;

    private void OpenPlotWindow()
    {
        if (_plotWindow != null)
        {
            _plotWindow.Activate();
            return;
        }
        _plotWindow = new PlotWindow(_plotViewModel);
        _plotWindow.Owner = System.Windows.Application.Current.MainWindow;
        _plotWindow.Closed += (_, _) => _plotWindow = null;
        _plotWindow.Show();
        StatusText = "Plot window opened - receiving numeric values from RX data";
    }

    private StatsWindow? _statsWindow;

    private void OpenStatsWindow()
    {
        if (_statsWindow != null)
        {
            _statsWindow.Activate();
            return;
        }
        _statsWindow = new StatsWindow { DataContext = _tool.StatsViewModel };
        _statsWindow.Owner = System.Windows.Application.Current.MainWindow;
        _statsWindow.Closed += (_, _) => _statsWindow = null;
        _statsWindow.Show();
    }

    private static List<double> ExtractNumericValues(string text)
    {
        var results = new List<double>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        foreach (Match m in KeyValueRegex.Matches(text))
        {
            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val))
                results.Add(val);
        }

        if (results.Count == 0)
        {
            foreach (Match m in StandaloneNumberRegex.Matches(text))
            {
                if (double.TryParse(m.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                    results.Add(val);
            }
        }

        return results;
    }

    public void SaveSettings(double windowX, double windowY, double windowWidth, double windowHeight)
    {
        _settings.WindowX = windowX;
        _settings.WindowY = windowY;
        _settings.WindowWidth = windowWidth;
        _settings.WindowHeight = windowHeight;
        _settings.IsDarkTheme = IsDarkTheme;
        _settings.Language = _connection.SelectedLanguage;
        _settings.LastPort = _connection.SelectedPort;
        _settings.LastBaudRate = _connection.SelectedBaudRate;
        _settings.LastDataBits = _connection.SelectedDataBits;
        _settings.IsHexSend = _dataFlow.IsHexSend;
        _settings.IsHexDisplayRx = _dataFlow.IsHexDisplayRx;
        _settings.IsHexDisplayTx = _dataFlow.IsHexDisplayTx;
        _settings.EnableRxTimestamp = _dataFlow.EnableRxTimestamp;
        _settings.EnableTxTimestamp = _dataFlow.EnableTxTimestamp;
        _settingsService.Save(_settings);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tool.Dispose();
        _connection.Dispose();
        _http.Dispose();
        _parserManager.Dispose();
        _multiPort.Dispose();
        _networkBridge.Dispose();
        _serial.Dispose();
        _sessionRecorder.Dispose();
        _logger.Dispose();
    }
}
