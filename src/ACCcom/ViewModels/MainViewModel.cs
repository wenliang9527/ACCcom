using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using ACCcom.Core.Collections;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class MainViewModel : ObservableObject, IDisposable
{
    private static readonly Regex KeyValueRegex = new(@"[=:]?\s*(-?\d+\.?\d*)", RegexOptions.Compiled);
    private static readonly Regex StandaloneNumberRegex = new(@"-?\d+\.\d+", RegexOptions.Compiled);
    private readonly ISerialService _serial;
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
    private readonly PortMonitorService _portMonitor = new();
    private readonly FrameAssemblerConfig _frameAssemblerConfig = new();
    private readonly PlotViewModel _plotViewModel = new();
    private readonly SettingsService _settingsService = new();
    private readonly HighlightService _highlightService = new();
    private readonly AutoBaudDetector _autoBaudDetector = new();
    private AppSettings _settings;
    private PlotWindow? _plotWindow;
    private bool _disposed;

    private readonly ConnectionViewModel _connection;
    private readonly DataFlowViewModel _dataFlow;
    private readonly ToolViewModel _tool;
    private readonly HighlightViewModel _highlights;
    private HighlightWindow? _highlightWindow;
    private ProtocolTestViewModel? _protocolTest;
    private ProtocolTestWindow? _protocolTestWindow;
    private VirtualSerialViewModel? _virtualSerial;
    private VirtualSerialWindow? _virtualSerialWindow;
    private TriggerWindow? _triggerWindow;
    private MacroWindow? _macroWindow;
    private ShortcutsWindow? _shortcutsWindow;

    private readonly ModbusConnectionManager _modbusConnectionManager = new();
    private readonly ModbusSlaveService _modbusSlaveService = new();
    private ModbusViewModel? _modbusViewModel;
    private ModbusWindow? _modbusWindow;

    private readonly Action<LogEntry> _serialDataHandler;
    private readonly Action<string> _serialErrorHandler;
    private readonly Action _serialDisconnectedHandler;
    private readonly Action<LogEntry> _networkDataHandler;
    private readonly Action<string> _networkErrorHandler;
    private readonly Action _networkDisconnectedHandler;
    private readonly Action<TriggerRule, LogEntry> _triggerFiredHandler;
    private readonly Action<string> _serialDeviceWaitHandler;
    private readonly Action<LogEntry> _multiPortDataHandler;
    private readonly System.Windows.Threading.DispatcherTimer? _statsTimer;
    private readonly System.Windows.Threading.DispatcherTimer? _recordingPollTimer;

    public ConnectionViewModel Connection => _connection;
    public DataFlowViewModel DataFlow => _dataFlow;
    public ToolViewModel Tool => _tool;

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    /// <summary>Advanced serial params row visibility (gear toggle in toolbar).</summary>
    private bool _showAdvanced;
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set => SetField(ref _showAdvanced, value);
    }

    private bool _showQuickSendSidebar = true;
    public bool ShowQuickSendSidebar
    {
        get => _showQuickSendSidebar;
        set
        {
            if (SetField(ref _showQuickSendSidebar, value))
            {
                _settings.ShowQuickSendSidebar = value;
                _settingsService.Save(_settings);
            }
        }
    }

    private bool _isDarkTheme;
    public bool IsDarkTheme { get => _isDarkTheme; set => SetField(ref _isDarkTheme, value); }

    // ===== Theme selection =====
    public sealed record ThemeOption(string Id, string Name, System.Windows.Media.Color Accent);

    private ObservableCollection<ThemeOption> _themes = new();
    public ObservableCollection<ThemeOption> Themes => _themes;

    private string _selectedTheme = "Dark";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Helpers.ThemeManager.Exists(value)) value = "Dark";
            if (SetField(ref _selectedTheme, value))
                ApplySelectedTheme();
        }
    }

    private void ApplySelectedTheme()
    {
        App.ApplyTheme(_selectedTheme);
        IsDarkTheme = _selectedTheme != "Light";
        _settings.Theme = _selectedTheme;
        _settings.IsDarkTheme = IsDarkTheme;
        _settingsService.Save(_settings);
    }

    private void BuildThemeOptions()
    {
        var selected = _selectedTheme;
        _themes = new ObservableCollection<ThemeOption>(
            Helpers.ThemeManager.ThemeIds.Select(id => new ThemeOption(
                id,
                Helpers.ThemeManager.GetDisplayName(id),
                Helpers.ThemeManager.GetAccent(id))));
        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void ToggleTheme()
    {
        SelectedTheme = Helpers.ThemeManager.NextOf(_selectedTheme);
    }

    private void ToggleRecording()
    {
        if (_sessionRecorder.IsRecording)
        {
            var path = _sessionRecorder.CurrentFile;
            var count = _sessionRecorder.RecordedCount;
            _sessionRecorder.StopRecording();
            StatusText = string.Format(LanguageManager.Instance["Status.RecordingStopped"], count, Path.GetFileName(path ?? ""));
        }
        else
        {
            if (_sessionRecorder.StartRecording())
            {
                var path = _sessionRecorder.CurrentFile ?? "";
                StatusText = string.Format(LanguageManager.Instance["Status.RecordingStarted"], Path.GetFileName(path));
            }
            else
            {
                StatusText = LanguageManager.Instance["Status.RecordingStartFailed"];
            }
        }
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(RecordedCount));
        OnPropertyChanged(nameof(RecordingFile));
    }

    private string _httpUrl = HttpService.DefaultUrl;
    public string HttpUrl { get => _httpUrl; set => SetField(ref _httpUrl, value); }

    public AppSettings Settings => _settings;

    /// <summary>True while the SessionRecorder is writing RX/TX entries to disk.
    /// Backed by a DispatcherTimer that polls the recorder so the UI updates
    /// even though SessionRecorder itself has no PropertyChanged surface.</summary>
    public bool IsRecording => _sessionRecorder.IsRecording;
    public int RecordedCount => _sessionRecorder.RecordedCount;
    public string? RecordingFile => _sessionRecorder.CurrentFile;

    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand OpenHighlightCommand { get; }
    public ICommand OpenProtocolTestCommand { get; }
    public ICommand OpenVirtualSerialCommand { get; }
    public ICommand OpenCompareCommand { get; }
    public ICommand OpenTriggerCommand { get; }
    public ICommand OpenShortcutsCommand { get; }
    public ICommand OpenRecordingsFolderCommand { get; }
    public HighlightViewModel Highlights => _highlights;
    public ProtocolTestViewModel? ProtocolTest => _protocolTest;

    public MainViewModel() : this(new SerialService()) { }

    public MainViewModel(ISerialService serial)
    {
        _serial = serial;
        // Startup profile: the ctor runs synchronously before the first frame is
        // shown, so each stage's cost is visible here to guide deferral work.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Stage(string name)
        {
            // Trace (not Debug): Debug is compiled out in Release, so these
            // startup timings would be invisible in the shipped exe.
            System.Diagnostics.Trace.WriteLine($"[startup] ctor: {name} = {sw.ElapsedMilliseconds}ms");
        }

        _settings = _settingsService.Load();
        _parserManager = new ParserManager(dispatch: action => System.Windows.Application.Current?.Dispatcher.BeginInvoke(action), parserCacheSize: _settings.ParserCacheSize);
        Stage("settings+parser");

        _http = new HttpService(new HttpServiceOptions
        {
            SerialService = _serial,
            ParserManager = _parserManager,
            SlaveService = _modbusSlaveService,
            MultiPortService = _multiPort,
            ModbusService = _modbusConnectionManager.GetDefaultService(_serial),
            ModbusConnections = _modbusConnectionManager,
            AutoBaudDetector = _autoBaudDetector,
            SessionRecorder = _sessionRecorder,
            DataStatistics = _stats,
            BufferCapacity = _settings.BufferCapacity,
            ApiToken = string.IsNullOrWhiteSpace(_settings.HttpApiToken) ? null : _settings.HttpApiToken
        });
        // _http.Start() is deliberately NOT called here: binding the 8899 port in
        // the ctor blocked the first frame and crashed startup when the port was
        // taken. MainWindow starts it after the window is shown (StartHttpAsync),
        // degrading to a status message instead of a hard failure.
        Stage("http construct");

        // _modbusViewModel 在 OpenModbusWindow 中延迟初始化

        _connection = new ConnectionViewModel(_serial, _networkBridge, _connectionManager, msg => StatusText = msg, _portMonitor, _autoBaudDetector);
        _dataFlow = new DataFlowViewModel(_serial, _networkBridge, _logger, _http, _triggerService, _parserManager, _frameAssemblerConfig, _stats, _fileExportService, msg => StatusText = msg, _settings, _highlightService);
        _highlights = new HighlightViewModel(_highlightService, () => _dataFlow, msg => StatusText = msg);
        _highlights.Load();
        _tool = new ToolViewModel(
            _serial, _networkBridge, _shortcutManager, _presetManager, _macroManager, _bookmarkManager,
            _multiPort, _triggerService, _sessionRecorder, _logger,
            msg => StatusText = msg,
            () => _connection.IsOpen,
            () => _dataFlow,
            () => _connection,
            () => OpenPlotWindow(),
            () => OpenStatsWindow());
        Stage("viewmodels");

        _connection.PropertyChanged += (_, e) => RaisePropertyChanged(e);
        _dataFlow.PropertyChanged += (_, e) => RaisePropertyChanged(e);
        _tool.PropertyChanged += (_, e) => RaisePropertyChanged(e);

        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording());
        OpenHighlightCommand = new RelayCommand(_ => OpenHighlightWindow());
        OpenProtocolTestCommand = new RelayCommand(_ => OpenProtocolTestWindow());
        OpenVirtualSerialCommand = new RelayCommand(_ => OpenVirtualSerialWindow());
        OpenCompareCommand = new RelayCommand(_ => OpenCompareWindow());
        OpenTriggerCommand = new RelayCommand(_ => OpenTriggerWindow());
        OpenMacroCommand = new RelayCommand(_ => OpenMacroWindow());
        OpenShortcutsCommand = new RelayCommand(_ => OpenShortcutsWindow());
        OpenRecordingsFolderCommand = new RelayCommand(_ => OpenRecordingsFolder());

        OpenFrameAssemblerConfigCommand = new RelayCommand(_ => OpenFrameAssemblerConfig());
        OpenDashboardCommand = new RelayCommand(_ => OpenDashboard());
        OpenModbusCommand = new RelayCommand(_ =>
        {
            try { OpenModbusWindow(); }
            catch (Exception ex) { System.Windows.MessageBox.Show($"MODBUS error:\n{ex}"); }
        });

        _serialDataHandler = _dataFlow.OnSerialData;
        _serialErrorHandler = msg => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        _serialDisconnectedHandler = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _connection.IsOpen = false; StatusText = LanguageManager.Instance["Status.PortDisconnected"]; });
        _serialDeviceWaitHandler = port => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            StatusText = string.Format(LanguageManager.Instance["Status.WaitingForDevice"], port));
        _networkDataHandler = _dataFlow.OnSerialData;
        _networkErrorHandler = msg => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusText = msg);
        _networkDisconnectedHandler = () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { _connection.IsOpen = false; StatusText = LanguageManager.Instance["Status.NetworkDisconnected"]; });
        _triggerFiredHandler = _tool.OnTriggerFired;

        _serial.OnDataReceived += _serialDataHandler;
        _serial.OnError += _serialErrorHandler;
        _serial.OnDisconnected += _serialDisconnectedHandler;
        _serial.OnDeviceWait += _serialDeviceWaitHandler;

        _networkBridge.OnDataReceived += _networkDataHandler;
        _networkBridge.OnError += _networkErrorHandler;
        _networkBridge.OnDisconnected += _networkDisconnectedHandler;

        _triggerService.OnTriggerFired += _triggerFiredHandler;

        OpenSchemaEditorCommand = new RelayCommand(_ => OpenSchemaEditor());

        _multiPortDataHandler = _dataFlow.OnSerialData;
        _multiPort.OnDataReceived += _multiPortDataHandler;

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
            // Pipe every accepted entry into the recorder; the recorder itself
            // drops writes when it's not actively recording so the cost is one
            // null-check per frame.
            _sessionRecorder.Record(entry);
            // Same for the protocol-test runner — its RX queue only grows when
            // a test is actually running (checked inside OnRxEntry).
            _protocolTest?.OnRxEntry(entry);
        };

        // Poll the recorder so IsRecording / RecordedCount surface in the UI.
        // The recorder doesn't raise change notifications of its own; a low-rate
        // timer keeps the binding fresh without coupling the service to WPF.
        _recordingPollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _recordingPollTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(RecordedCount));
            OnPropertyChanged(nameof(RecordingFile));
        };
        _recordingPollTimer.Start();

        HttpUrl = HttpService.DefaultUrl;

        SelectedBaudRate = _settings.LastBaudRate;
        SelectedDataBits = _settings.LastDataBits;
        IsHexSend = _settings.IsHexSend;
        IsHexDisplayRx = _settings.IsHexDisplayRx;
        IsHexDisplayTx = _settings.IsHexDisplayTx;
        EnableRxTimestamp = _settings.EnableRxTimestamp;
        EnableTxTimestamp = _settings.EnableTxTimestamp;
        IsDarkTheme = _settings.IsDarkTheme;
        // Restore theme: new string setting wins; fall back to legacy bool.
        _selectedTheme = !string.IsNullOrEmpty(_settings.Theme) && Helpers.ThemeManager.Exists(_settings.Theme)
            ? _settings.Theme
            : (_settings.IsDarkTheme ? "Dark" : "Light");
        App.ApplyTheme(_selectedTheme);
        BuildThemeOptions();
        Stage("theme");
        LanguageManager.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is "Item[]" or "Item") BuildThemeOptions();
        };
        _showQuickSendSidebar = _settings.ShowQuickSendSidebar;
        _connection.SelectedLanguage = _settings.Language;
        LanguageManager.Instance.LoadLanguage(_settings.Language);
        Stage("language");
        if (!string.IsNullOrEmpty(_settings.LastPort) && _connection.AvailablePorts.Contains(_settings.LastPort))
            _connection.SelectedPort = _settings.LastPort;

        _statsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) =>
        {
            RxRate = $"{_stats.RxBytesPerSecond:F1} B/s | {_stats.RxFramesPerSecond:F1} fps";
            TxRate = $"{_stats.TxBytesPerSecond:F1} B/s | {_stats.TxFramesPerSecond:F1} fps";
            ErrorRate = $"{_stats.ErrorRate:F1}%";
            FrameInterval = $"{_stats.AvgFrameIntervalMs:F1} ms";
            // Counters accumulate silently on 30ms flushes; surface them here at
            // 1Hz so the status bar bindings don't re-layout on every flush tick.
            _dataFlow.NotifyCountsChanged();
            _tool.StatsViewModel?.Update(_stats, RxByteCount, TxByteCount, RxCount, TxCount, ConnectionDuration);
        };
        _statsTimer.Start();
        Stage("ctor total");
    }

    public async Task InitializeAsync()
    {
        // Shortcuts/presets/macros read separate files with no data dependency;
        // run them in parallel, then load triggers (synchronous file read) off
        // the calling thread.
        await Task.WhenAll(
            _tool.LoadShortcutsAsync(),
            _tool.LoadPresetsAsync(),
            _tool.LoadMacrosAsync());
        await Task.Run(() => _tool.LoadTriggers());
    }

    /// <summary>Starts the HTTP API (port 8899) after the first frame is shown.
    /// A port conflict or bind error degrades to a status-bar message instead of
    /// crashing startup (the old in-ctor Start() threw and killed the app).</summary>
    public void StartHttpAsync()
    {
        Task.Run(() =>
        {
            try
            {
                _http.Start();
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    StatusText = string.Format(LanguageManager.Instance["Status.HttpStartFailed"], ex.Message));
            }
        });
    }

    private void OpenShortcutsWindow()
    {
        if (_shortcutsWindow != null)
        {
            _shortcutsWindow.Activate();
            return;
        }
        _shortcutsWindow = new ShortcutsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _shortcutsWindow.Closed += (_, _) => _shortcutsWindow = null;
        _shortcutsWindow.Show();
        StatusText = LanguageManager.Instance["Status.ShortcutsOpened"];
    }

    /// <summary>Reveals the recordings folder in Explorer. Called from the REC
    /// indicator's context menu so a finished session can be inspected without
    /// navigating the file system by hand.</summary>
    private void OpenRecordingsFolder()
    {
        try
        {
            Directory.CreateDirectory(SessionRecorder.RecordingsDirectory);
            System.Diagnostics.Process.Start("explorer.exe", SessionRecorder.RecordingsDirectory);
            StatusText = LanguageManager.Instance["Status.OpenRecordingsFolder"];
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LanguageManager.Instance["Status.RecordingsDirFailed"], ex.Message);
        }
    }

    private void OpenTriggerWindow()
    {
        if (_triggerWindow != null)
        {
            _triggerWindow.Activate();
            return;
        }
        _triggerWindow = new TriggerWindow(_tool.Triggers)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _triggerWindow.Closed += (_, _) => _triggerWindow = null;
        _triggerWindow.Show();
        StatusText = LanguageManager.Instance["Status.TriggersOpened"];
    }

    private void OpenMacroWindow()
    {
        if (_macroWindow != null)
        {
            _macroWindow.Activate();
            return;
        }
        _macroWindow = new MacroWindow(_tool.MacrosVm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _macroWindow.Closed += (_, _) => _macroWindow = null;
        _macroWindow.Show();
        StatusText = LanguageManager.Instance["Status.MacrosOpened"];
    }

    private void OpenProtocolTestWindow()
    {
        if (_protocolTestWindow != null)
        {
            _protocolTestWindow.Activate();
            return;
        }

        _protocolTest ??= new ProtocolTestViewModel(
            new ProtocolTestRunner(),
            _serial,
            () => _connection.IsOpen,
            msg => StatusText = msg);

        _protocolTestWindow = new ProtocolTestWindow(_protocolTest)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _protocolTestWindow.Closed += (_, _) =>
        {
            _protocolTestWindow = null;
            _protocolTest?.Dispose();
            _protocolTest = null;
        };
        _protocolTestWindow.Show();
        StatusText = LanguageManager.Instance["Status.ProtocolTestOpened"];
    }

    private void OpenVirtualSerialWindow()
    {
        if (_virtualSerialWindow != null)
        {
            _virtualSerialWindow.Activate();
            return;
        }

        _virtualSerial ??= new VirtualSerialViewModel(_dataFlow, msg => StatusText = msg);

        _virtualSerialWindow = new VirtualSerialWindow(_virtualSerial)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _virtualSerialWindow.Closed += (_, _) =>
        {
            _virtualSerialWindow = null;
            _virtualSerial?.Dispose();
            _virtualSerial = null;
        };
        _virtualSerialWindow.Show();
        StatusText = LanguageManager.Instance["Status.VirtualSerialOpened"];
    }

    private CompareWindow? _compareWindow;

    private void OpenCompareWindow()
    {
        if (_compareWindow != null)
        {
            _compareWindow.Activate();
            return;
        }

        _compareWindow = new CompareWindow
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _compareWindow.Closed += (_, _) => _compareWindow = null;
        _compareWindow.Show();
    }

    private void OpenHighlightWindow()
    {
        if (_highlightWindow != null)
        {
            _highlightWindow.Activate();
            return;
        }
        _highlightWindow = new HighlightWindow(_highlights)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        _highlightWindow.Closed += (_, _) => _highlightWindow = null;
        _highlightWindow.Show();
        StatusText = LanguageManager.Instance["Status.HighlightWindowOpened"];
    }

    public void NavigateHistory(int direction) => _dataFlow.NavigateHistory(direction);

    /// <summary>Non-mutating history navigation used by the view to restore text and
    /// place the caret at the end of the restored entry. The legacy <see cref="NavigateHistory"/>
    /// remains for any external callers (MCP tools, etc.).</summary>
    public bool TryNavigateHistory(int direction, out string? text, out int caretIndex)
        => _dataFlow.TryNavigateHistory(direction, out text, out caretIndex);

    public void SendShortcutByIndex(int index) => _tool.Shortcuts.SendByIndex(index);

    public ObservableRangeCollection<LogEntry> RxEntries => _dataFlow.RxEntries;
    public ObservableRangeCollection<LogEntry> TxEntries => _dataFlow.TxEntries;
    public ObservableCollection<string> AvailablePorts => _connection.AvailablePorts;
    public ObservableCollection<ConnectionViewModel.PortOption> PortOptions => _connection.PortOptions;
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
    public string TxRate { get => _dataFlow.TxRate; set => _dataFlow.TxRate = value; }
    public string ErrorRate { get => _dataFlow.ErrorRate; set => _dataFlow.ErrorRate = value; }
    public string FrameInterval { get => _dataFlow.FrameInterval; set => _dataFlow.FrameInterval = value; }
    public string RxFilterText { get => _dataFlow.RxFilterText; set => _dataFlow.RxFilterText = value; }
    public string TxFilterText { get => _dataFlow.TxFilterText; set => _dataFlow.TxFilterText = value; }
    public bool IsRegexFilter { get => _dataFlow.IsRegexFilter; set => _dataFlow.IsRegexFilter = value; }
    public bool UseExpressionFilter { get => _dataFlow.UseExpressionFilter; set => _dataFlow.UseExpressionFilter = value; }
    public bool JumpToRxMatch(bool forward) => _dataFlow.JumpToMatch(forward);
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

    public ShortcutPage? CurrentShortcutPage => _tool.CurrentShortcutPage;
    public ViewModels.ShortcutViewModel Shortcuts => _tool.Shortcuts;
    public ObservableCollection<SerialPreset> Presets => _tool.Presets;
    public SerialPreset? SelectedPreset { get => _tool.SelectedPreset; set => _tool.SelectedPreset = value; }
    public ObservableCollection<MacroTemplate> Macros => _tool.Macros;
    public MacroTemplate? SelectedMacro { get => _tool.SelectedMacro; set => _tool.SelectedMacro = value; }
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
    public ICommand AutoDetectBaudCommand => _connection.AutoDetectBaudCommand;
    public ICommand SendCommand => _dataFlow.SendCommand;
    public ICommand ClearRxCommand => _dataFlow.ClearRxCommand;
    public ICommand ClearTxCommand => _dataFlow.ClearTxCommand;
    public ICommand SaveRxCommand => _dataFlow.SaveRxCommand;
    public ICommand SaveTxCommand => _dataFlow.SaveTxCommand;
    public ICommand SaveRxJsonCommand => _dataFlow.SaveRxJsonCommand;
    public ICommand SaveTxJsonCommand => _dataFlow.SaveTxJsonCommand;
    public ICommand SaveRxCsvCommand => _dataFlow.SaveRxCsvCommand;
    public ICommand SaveTxCsvCommand => _dataFlow.SaveTxCsvCommand;
    public ICommand SaveRxPcapCommand => _dataFlow.SaveRxPcapCommand;
    public ICommand SaveTxPcapCommand => _dataFlow.SaveTxPcapCommand;
    public ICommand OpenParserDirCommand => _dataFlow.OpenParserDirCommand;
    public ICommand CompareFramesCommand => _dataFlow.CompareFramesCommand;
    public ICommand OpenFrameAssemblerConfigCommand { get; }
    public ICommand SendShortcutCommand => _tool.SendShortcutCommand;
    public ICommand AddShortcutCommand => _tool.AddShortcutCommand;
    public ICommand SavePresetCommand => _tool.SavePresetCommand;
    public ICommand DeletePresetCommand => _tool.DeletePresetCommand;
    public ICommand RunMacroCommand => _tool.RunMacroCommand;
    public ICommand StopMacroCommand => _tool.StopMacroCommand;
    public ICommand SaveMacroCommand => _tool.SaveMacroCommand;
    public ICommand LoadMacroCommand => _tool.LoadMacroCommand;
    public ICommand AddMacroCommand => _tool.AddMacroCommand;
    public ICommand DeleteMacroCommand => _tool.DeleteMacroCommand;
    public ICommand AddMacroStepCommand => _tool.AddMacroStepCommand;
    public ICommand RemoveMacroStepCommand => _tool.RemoveMacroStepCommand;
    public ICommand OpenMacroCommand { get; }
    public ICommand OpenMultiPortCommand => _tool.OpenMultiPortCommand;
    public ICommand CloseMultiPortCommand => _tool.CloseMultiPortCommand;
    public ICommand CloseAllPortsCommand => _tool.CloseAllPortsCommand;
    public ICommand SaveTriggersCommand => _tool.SaveTriggersCommand;
    public ICommand LoadTriggersCommand => _tool.LoadTriggersCommand;
    public ICommand AddTriggerCommand => _tool.AddTriggerCommand;
    public ICommand DeleteTriggerCommand => _tool.DeleteTriggerCommand;
    public ICommand AddBookmarkCommand => _tool.AddBookmarkCommand;
    public ICommand JumpToBookmarkCommand => _tool.JumpToBookmarkCommand;
    public ICommand RemoveBookmarkCommand => _tool.RemoveBookmarkCommand;
    public ICommand NextBookmarkCommand => _tool.NextBookmarkCommand;
    public ICommand PrevBookmarkCommand => _tool.PrevBookmarkCommand;
    public ICommand ReplayFileCommand => _tool.ReplayFileCommand;
    public ICommand StopLoopCommand => _tool.StopLoopCommand;
    public ICommand OpenPlotCommand => _tool.OpenPlotCommand;
    public ICommand OpenStatsCommand => _tool.OpenStatsCommand;
    public ICommand OpenSchemaEditorCommand { get; }
    public ICommand OpenModbusCommand { get; }
    public ICommand OpenDashboardCommand { get; }

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
        StatusText = LanguageManager.Instance["Status.PlotWindowOpened"];
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

    private void OpenModbusWindow()
    {
        if (_modbusWindow != null)
        {
            _modbusWindow.Activate();
            return;
        }

        if (_modbusViewModel == null)
        {
            var defaultSvc = _modbusConnectionManager.GetDefaultService(_serial);
            var dialog = new ModbusConnectionDialog(_modbusConnectionManager, defaultSvc, _serial)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            if (dialog.ShowDialog() != true) return;

            var result = dialog.Result;
            if (result == null) return;
            _modbusViewModel = new ModbusViewModel(result, msg => StatusText = msg);
            _modbusViewModel.SetSlaveService(_modbusSlaveService);
        }

        _modbusWindow = new ModbusWindow(_modbusViewModel);
        _modbusWindow.Owner = System.Windows.Application.Current.MainWindow;
        _modbusWindow.Closed += (_, _) =>
        {
            _modbusWindow = null;
            _modbusViewModel = null;
        };
        _modbusWindow.Show();
        StatusText = LanguageManager.Instance["Status.ModbusMasterOpened"];
    }

    private void OpenSchemaEditor()
    {
        var editorVm = new SchemaEditorViewModel(_parserManager);
        var window = new SchemaEditorWindow(editorVm);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();
    }

    private void OpenFrameAssemblerConfig()
    {
        var window = new FrameAssemblerConfigWindow(_frameAssemblerConfig)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (window.ShowDialog() == true)
        {
            // Frame assembly settings changed: rebuild the FrameBuffer so the
            // new header/length-field/timeout take effect immediately.
            _dataFlow.ApplyFrameConfig();
            StatusText = _frameAssemblerConfig.Enabled
                ? string.Format(LanguageManager.Instance["Status.FrameAssemblyEnabled"], _frameAssemblerConfig.Header)
                : LanguageManager.Instance["Status.FrameAssemblyDisabled"];
        }
    }

    /// <summary>Opens the embedded web dashboard in the system default browser.
    /// The HTTP server runs on the loopback address with an API token, so this
    /// only ever targets the local machine.</summary>
    private void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo(HttpUrl + "/dashboard/") { UseShellExecute = true });
            StatusText = string.Format(LanguageManager.Instance["Status.DashboardOpened"], HttpUrl);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LanguageManager.Instance["Status.DashboardFailed"], ex.Message);
        }
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

    public void SaveSettings(double windowX, double windowY, double windowWidth, double windowHeight, double sidebarWidth)
    {
        _settings.WindowX = windowX;
        _settings.WindowY = windowY;
        _settings.WindowWidth = windowWidth;
        _settings.WindowHeight = windowHeight;
        _settings.QuickSendSidebarWidth = sidebarWidth > 0 ? sidebarWidth : _settings.QuickSendSidebarWidth;
        _settings.Theme = _selectedTheme;
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
        // Persist the in-memory send history (newest last) back to settings.
        _dataFlow.PersistSendHistory();
        _settingsService.Save(_settings);
    }

    /// <summary>Called by <see cref="Controls.DataPanel"/> when the user finishes
    /// resizing a column. Replaces the in-memory map; persistence happens on
    /// <see cref="SaveSettings"/> during window close.</summary>
    public void UpdateFieldGridColumnWidths(Dictionary<int, double> widths)
    {
        _settings.FieldGridColumnWidths = new Dictionary<int, double>(widths);
    }

    /// <summary>Returns the saved field-grid column widths (or null when none).
    /// The view calls this once after the DataGrid is loaded.</summary>
    public IReadOnlyDictionary<int, double>? GetFieldGridColumnWidths()
        => _settings.FieldGridColumnWidths is { Count: > 0 }
            ? _settings.FieldGridColumnWidths
            : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _statsTimer?.Stop();
        _recordingPollTimer?.Stop();
        _serial.OnDataReceived -= _serialDataHandler;
        _serial.OnError -= _serialErrorHandler;
        _serial.OnDisconnected -= _serialDisconnectedHandler;
        _serial.OnDeviceWait -= _serialDeviceWaitHandler;
        _networkBridge.OnDataReceived -= _networkDataHandler;
        _networkBridge.OnError -= _networkErrorHandler;
        _networkBridge.OnDisconnected -= _networkDisconnectedHandler;
        _triggerService.OnTriggerFired -= _triggerFiredHandler;
        _multiPort.OnDataReceived -= _multiPortDataHandler;

        _tool.Dispose();
        _connection.Dispose();
        _dataFlow.Dispose();
        _http.Dispose();
        _parserManager.Dispose();
        _multiPort.Dispose();
        _networkBridge.Dispose();
        _serial.Dispose();
        _sessionRecorder.Dispose();
        _logger.Dispose();
        _modbusViewModel?.Dispose();
        _modbusConnectionManager.Dispose();
        _modbusSlaveService.Dispose();
        _portMonitor.Dispose();
        _autoBaudDetector.Dispose();
    }
}
