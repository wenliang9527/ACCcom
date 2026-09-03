using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ACCcom.Core.Collections;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class DataFlowViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private int MaxEntries => _settings?.MaxDisplayEntries ?? 10000;
    private readonly ISerialService _serial;
    private readonly NetworkBridgeService _networkBridge;
    private readonly LoggerService _logger;
    private readonly HttpService _http;
    private readonly TriggerService _triggerService;
    private readonly ParserManager _parserManager;
    private readonly FrameAssembler _frameAssembler;
    private readonly FrameBuffer _frameBuffer;
    private readonly AutoParserMatcher _autoMatcher;
    private readonly DataStatistics _stats;
    private readonly FileExportService _fileExportService;
    private readonly PcapExportService _pcapExportService = new();
    private readonly Action<string> _setStatus;
    private readonly AppSettings _settings;
    private readonly HighlightService? _highlightService;
    private readonly DispatcherTimer? _filterDebounce;

    private readonly List<string> _sendHistory = new();
    private int _historyIndex = -1;
    private int _sendCounter;

    // Batched UI updates: serial/network events arrive on background threads and
    // at high rates; entries are queued here and flushed to the observable
    // collections in one ranged Add per tick (see FlushPendingEntries).
    private const int TrimChunkSize = 100;
    private readonly List<LogEntry> _pendingRx = new();

    // Recent RX text used by the macro engine's WaitFor/Condition matching.
    // Locked so the macro runner (background polling) can read safely while RX
    // entries keep arriving. Cleared when a macro starts so waits only see data
    // received during that run.
    private readonly object _recentRxLock = new();
    private readonly List<string> _recentRxTexts = new();
    private const int RecentRxTextCap = 512;
    private readonly List<LogEntry> _pendingTx = new();
    private readonly DispatcherTimer? _flushTimer;
    private readonly Action<LogEntry> _frameBufferFrameHandler;
    private readonly Action<string> _frameBufferErrorHandler;
    private readonly Action<string> _parserReloadedHandler;

    public ObservableRangeCollection<LogEntry> RxEntries { get; } = new();
    public ObservableRangeCollection<LogEntry> TxEntries { get; } = new();

    private string _sendText = "";
    public string SendText
    {
        get => _sendText;
        set
        {
            if (SetField(ref _sendText, value ?? ""))
            {
                UpdateHexValidation();
            }
        }
    }

    private bool _isHexSend;
    public bool IsHexSend
    {
        get => _isHexSend;
        set
        {
            if (SetField(ref _isHexSend, value))
            {
                UpdateHexValidation();
            }
        }
    }

    /// <summary>True when the current send input is acceptable to transmit (text or valid hex).</summary>
    public bool IsSendInputValid => !IsHexSend || _hexValidation.IsValid;

    /// <summary>User-facing message describing why the hex input is invalid; empty when valid.</summary>
    public string HexValidationError
    {
        get => _hexValidation.IsValid ? "" : DescribeHexError(_hexValidation);
    }

    private HexHelper.HexValidationResult _hexValidation = new(isValid: true, invalidIndex: -1, byteCount: 0);

    private void UpdateHexValidation()
    {
        var next = IsHexSend
            ? HexHelper.ValidateHexInput(_sendText)
            : new HexHelper.HexValidationResult(isValid: true, invalidIndex: -1, byteCount: 0);
        if (next.IsValid != _hexValidation.IsValid ||
            next.InvalidIndex != _hexValidation.InvalidIndex ||
            next.ByteCount != _hexValidation.ByteCount)
        {
            _hexValidation = next;
            OnPropertyChanged(nameof(IsSendInputValid));
            OnPropertyChanged(nameof(HexValidationError));
        }
    }

    private static string DescribeHexError(HexHelper.HexValidationResult r)
    {
        if (r.InvalidIndex >= 0) return $"HEX 非法字符(位置 {r.InvalidIndex + 1})";
        return "HEX 必须成对(偶数位)";
    }

    /// <summary>Recent send-box entries, oldest first. Backing field for UI binding (dropdown of history).</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> SendHistory { get; } = new();

    private bool _isHexDisplayRx;
    public bool IsHexDisplayRx { get => _isHexDisplayRx; set => SetField(ref _isHexDisplayRx, value); }

    private bool _isHexDisplayTx;
    public bool IsHexDisplayTx { get => _isHexDisplayTx; set => SetField(ref _isHexDisplayTx, value); }

    public ICommand ToggleHexDisplayCommand { get; }

    private bool _enableRxTimestamp = true;
    public bool EnableRxTimestamp { get => _enableRxTimestamp; set => SetField(ref _enableRxTimestamp, value); }

    private bool _enableTxTimestamp = true;
    public bool EnableTxTimestamp { get => _enableTxTimestamp; set => SetField(ref _enableTxTimestamp, value); }

    private int _rxCount;
    public int RxCount { get => _rxCount; set => SetField(ref _rxCount, value); }

    private int _txCount;
    public int TxCount { get => _txCount; set => SetField(ref _txCount, value); }

    private int _rxByteCount;
    public int RxByteCount { get => _rxByteCount; set => SetField(ref _rxByteCount, value); }

    private int _txByteCount;
    public int TxByteCount { get => _txByteCount; set => SetField(ref _txByteCount, value); }

    private int _errorFrameCount;
    public int ErrorFrameCount { get => _errorFrameCount; set => SetField(ref _errorFrameCount, value); }

    private string _rxRate = "";
    public string RxRate { get => _rxRate; set => SetField(ref _rxRate, value); }

    private string _txRate = "";
    public string TxRate { get => _txRate; set => SetField(ref _txRate, value); }

    private string _errorRate = "";
    public string ErrorRate { get => _errorRate; set => SetField(ref _errorRate, value); }

    private string _frameInterval = "";
    public string FrameInterval { get => _frameInterval; set => SetField(ref _frameInterval, value); }

    private string _rxFilterText = "";
    public string RxFilterText { get => _rxFilterText; set { if (SetField(ref _rxFilterText, value)) DebounceFilter(); } }

    private string _txFilterText = "";
    public string TxFilterText { get => _txFilterText; set { if (SetField(ref _txFilterText, value)) DebounceFilter(); } }

    private bool _isRegexFilter;
    public bool IsRegexFilter { get => _isRegexFilter; set { if (SetField(ref _isRegexFilter, value)) { FilteredRxEntries?.Refresh(); FilteredTxEntries?.Refresh(); } } }

    private bool _useExpressionFilter;
    /// <summary>Filter matching via PacketFilter expression syntax ("text contains OK and direction==RX").</summary>
    public bool UseExpressionFilter
    {
        get => _useExpressionFilter;
        set
        {
            if (SetField(ref _useExpressionFilter, value))
            {
                RebuildFilterEngines();
                FilteredRxEntries?.Refresh();
                FilteredTxEntries?.Refresh();
            }
        }
    }

    private PacketFilterEngine? _rxExpressionEngine;
    private PacketFilterEngine? _txExpressionEngine;

    private void RebuildFilterEngines()
    {
        _rxExpressionEngine = _useExpressionFilter && !string.IsNullOrWhiteSpace(_rxFilterText) ? new PacketFilterEngine(_rxFilterText) : null;
        _txExpressionEngine = _useExpressionFilter && !string.IsNullOrWhiteSpace(_txFilterText) ? new PacketFilterEngine(_txFilterText) : null;
    }

    private bool _showRx = true;
    public bool ShowRx { get => _showRx; set { if (SetField(ref _showRx, value)) FilteredRxEntries?.Refresh(); } }

    private void DebounceFilter()
    {
        if (_filterDebounce == null) return;
        _filterDebounce.IsEnabled = false;
        _filterDebounce.IsEnabled = true;
    }

    private bool _showTx = true;
    public bool ShowTx { get => _showTx; set { if (SetField(ref _showTx, value)) FilteredTxEntries?.Refresh(); } }

    public ListCollectionView? FilteredRxEntries { get; private set; }
    public ListCollectionView? FilteredTxEntries { get; private set; }

    private bool _autoScrollRx = true;
    public bool AutoScrollRx { get => _autoScrollRx; set => SetField(ref _autoScrollRx, value); }

    private bool _autoScrollTx = true;
    public bool AutoScrollTx { get => _autoScrollTx; set => SetField(ref _autoScrollTx, value); }

    private string _selectedParser = ParserManager.NoParserName;
    public string SelectedParser
    {
        get => _selectedParser;
        set
        {
            if (SetField(ref _selectedParser, value))
            {
                if (!_parserManager.Activate(value))
                    _setStatus(string.Format(LanguageManager.Instance["Status.ParserLoadFailed"], _parserManager.LastError));
                else if (value != ParserManager.NoParserName)
                    _setStatus(string.Format(LanguageManager.Instance["Status.ParserSelected"], value));
            }
        }
    }

    public ObservableCollection<string> AvailableParsers => _parserManager.AvailableParsers;

    private LogEntry? _selectedEntry;
    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
                OnPropertyChanged(nameof(HasFields));
        }
    }

    /// <summary>Step the selection to the next/previous RX entry that matches the
    /// current filter. Used by F3 / Shift+F3 in MainWindow. The filter is whatever
    /// the user typed in the RX search box — "no filter" is treated as no match,
    /// which mirrors how the data panel already highlights matches.</summary>
    public bool JumpToMatch(bool forward)
    {
        if (FilteredRxEntries == null) return false;
        var target = MatchIndexNavigator.Step(FilteredRxEntries.Cast<LogEntry>(), e => e.IsSearchMatch, SelectedEntry, forward);
        if (target == null) return false;
        SelectedEntry = target;
        return true;
    }

    public bool HasFields => SelectedEntry?.Fields is { Count: > 0 };

    public Action<LogEntry>? OnRxProcessed { get; set; }
    public Action<LogEntry, int>? OnEntryProcessed { get; set; }

    public ICommand SendCommand { get; }
    public ICommand ClearRxCommand { get; }
    public ICommand ClearTxCommand { get; }
    public ICommand SaveRxCommand { get; }
    public ICommand SaveTxCommand { get; }
    public ICommand SaveRxJsonCommand { get; }
    public ICommand SaveTxJsonCommand { get; }
    public ICommand SaveRxCsvCommand { get; }
    public ICommand SaveTxCsvCommand { get; }
    public ICommand SaveRxPcapCommand { get; }
    public ICommand SaveTxPcapCommand { get; }
    public ICommand OpenParserDirCommand { get; }
    public ICommand CompareFramesCommand { get; }
    public ICommand ResetCountersCommand { get; }
    public ICommand ClearSendHistoryCommand { get; }

    public DataFlowViewModel(
        ISerialService serial,
        NetworkBridgeService networkBridge,
        LoggerService logger,
        HttpService http,
        TriggerService triggerService,
        ParserManager parserManager,
        FrameAssemblerConfig frameAssemblerConfig,
        DataStatistics stats,
        FileExportService fileExportService,
        Action<string> setStatus,
        AppSettings settings,
        HighlightService? highlightService = null)
    {
        _serial = serial;
        _networkBridge = networkBridge;
        _logger = logger;
        _http = http;
        _triggerService = triggerService;
        _parserManager = parserManager;
        _highlightService = highlightService;
        _frameAssembler = new FrameAssembler(frameAssemblerConfig, parserManager);
        _frameAssembler.OnFrameAssembled += OnAssembledFrame;
        _stats = stats;
        _fileExportService = fileExportService;
        _setStatus = setStatus;
        _settings = settings;

        _autoMatcher = new AutoParserMatcher();
        LoadParserFingerprints();
        _parserReloadedHandler = _ => LoadParserFingerprints();
        _parserManager.OnParserReloaded += _parserReloadedHandler;

        var bufferConfig = new FrameBufferConfig
        {
            Strategy = FrameExtractStrategy.ByHeader,
            Header = new byte[] { 0xA5, 0x5A },
            LengthFieldOffset = 2,
            LengthFieldSize = 1,
            LengthFieldIncludes = 4,
            MaxFrameSize = 4096,
            BufferCapacity = 65536,
            PartialFrameTimeoutMs = 2000
        };
        _frameBuffer = new FrameBuffer(bufferConfig, _autoMatcher, _parserManager);
        _frameBufferFrameHandler = OnFrameReady;
        _frameBuffer.OnFrameAssembled += _frameBufferFrameHandler;
        _frameBufferErrorHandler = msg => _setStatus(msg);
        _frameBuffer.OnError += _frameBufferErrorHandler;

        _filterDebounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
            IsEnabled = false
        };
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.IsEnabled = false;
            RebuildFilterEngines();
            FilteredRxEntries?.Refresh();
            FilteredTxEntries?.Refresh();
        };

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(30),
            IsEnabled = true
        };
        _flushTimer.Tick += (_, _) => FlushPendingEntries();

        SendCommand = new RelayCommand(_ => SendData());
        ClearRxCommand = new RelayCommand(_ => { FlushPendingEntries(); RxEntries.Clear(); _pendingRx.Clear(); RxCount = 0; RxByteCount = 0; });
        ClearTxCommand = new RelayCommand(_ => { FlushPendingEntries(); TxEntries.Clear(); _pendingTx.Clear(); TxCount = 0; TxByteCount = 0; });
        SaveRxCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToFile(RxEntries, "RX"); });
        SaveTxCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToFile(TxEntries, "TX"); });
        SaveRxJsonCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToJson(RxEntries, "RX"); });
        SaveTxJsonCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToJson(TxEntries, "TX"); });
        SaveRxCsvCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToCsv(RxEntries, "RX"); });
        SaveTxCsvCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToCsv(TxEntries, "TX"); });
        SaveRxPcapCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToPcap(RxEntries, "RX"); });
        SaveTxPcapCommand = new RelayCommand(_ => { FlushPendingEntries(); SaveToPcap(TxEntries, "TX"); });
        OpenParserDirCommand = new RelayCommand(_ => OpenParserDir());
        CompareFramesCommand = new RelayCommand(_ => OpenDiffWindow());
        ResetCountersCommand = new RelayCommand(_ =>
        {
            RxByteCount = 0;
            TxByteCount = 0;
            ErrorFrameCount = 0;
            _stats?.Reset();
        });
        ClearSendHistoryCommand = new RelayCommand(_ => { _sendHistory.Clear(); SendHistory.Clear(); PersistSendHistory(); });
        ToggleHexDisplayCommand = new RelayCommand(_ => { IsHexDisplayRx = !IsHexDisplayRx; IsHexDisplayTx = !IsHexDisplayTx; });

        // Hydrate persistent send history into the in-memory list and the UI collection.
        if (_settings.SendHistory is { Count: > 0 })
        {
            _sendHistory.AddRange(_settings.SendHistory);
            foreach (var item in _sendHistory) SendHistory.Add(item);
        }

        FilteredRxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(RxEntries);
        FilteredRxEntries.Filter = o => FilterEntry((LogEntry)o, _rxFilterText, _isRegexFilter, _showRx, _rxExpressionEngine);
        FilteredTxEntries = (ListCollectionView)CollectionViewSource.GetDefaultView(TxEntries);
        FilteredTxEntries.Filter = o => FilterEntry((LogEntry)o, _txFilterText, _isRegexFilter, _showTx, _txExpressionEngine);
    }

    public void OnSerialData(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";

            if (_frameAssembler.IsEnabled)
            {
                _frameAssembler.Feed(entry);
                return;
            }

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
            {
                try
                {
                    // Single hex pass: reuse the parsed bytes for both count and feed.
                    var bytes = HexHelper.HexStringToBytes(entry.RawHex);
                    byteCount = bytes.Length;
                    if (byteCount > 0)
                        _frameBuffer.Write(bytes);
                }
                catch { }
            }

            // Serial events arrive on a background thread; collection updates
            // must be marshaled to the UI thread.
            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (entry.Direction == "RX")
                    {
                        _stats.RecordRx(byteCount);
                        AddRxEntry(entry, byteCount);
                        OnRxProcessed?.Invoke(entry);
                    }
                    else
                    {
                        AddTxEntry(entry, byteCount);
                    }

                    OnEntryProcessed?.Invoke(entry, byteCount);
                }
                catch (Exception ex)
                {
                    _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
        }
    }

    private void OnAssembledFrame(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";
            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            _ = Task.Run(async () =>
            {
                try { await ProcessAssembledFrameAsync(entry, byteCount).ConfigureAwait(false); }
                catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message)); }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
        }
    }

    private void OnFrameReady(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = "main";

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _logger.Write(entry);

                    if (entry.Direction == "RX")
                    {
                        _stats.RecordRx(byteCount);
                        if (HexHelper.HasErrorSeverity(entry.Fields))
                            _stats.RecordError();
                        AddRxEntry(entry, byteCount);
                        OnRxProcessed?.Invoke(entry);
                    }
                    else
                    {
                        AddTxEntry(entry, byteCount);
                    }

                    OnEntryProcessed?.Invoke(entry, byteCount);
                }
                catch (Exception ex)
                {
                    _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
        }
    }

    private async Task ProcessAssembledFrameAsync(LogEntry entry, int byteCount)
    {
        await ProcessEntryAsync(entry, byteCount, forceRx: true, errorContext: "Status.ErrorProcessingFrame").ConfigureAwait(false);
    }

    private async Task ProcessEntryAsync(LogEntry entry, int byteCount, bool forceRx, string errorContext)
    {
        try
        {
            bool isRx = forceRx || entry.Direction == "RX";
            if (isRx && _parserManager.ActiveParserName != null)
                await RunParserAsync(entry).ConfigureAwait(false);

            _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    _logger.Write(entry);

                    if (isRx)
                    {
                        _stats.RecordRx(byteCount);
                        if (HexHelper.HasErrorSeverity(entry.Fields))
                            _stats.RecordError();
                        AddRxEntry(entry, byteCount);
                        OnRxProcessed?.Invoke(entry);
                    }
                    else
                    {
                        AddTxEntry(entry, byteCount);
                    }

                    OnEntryProcessed?.Invoke(entry, byteCount);
                }
                catch (Exception ex)
                {
                    _setStatus(string.Format(LanguageManager.Instance[errorContext], ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance[errorContext], ex.Message));
        }
    }

    /// <summary>Queues an RX entry; the UI collection is updated in batches by the flush timer.</summary>
    public void AddRxEntry(LogEntry entry, int byteCount)
    {
        ApplyHighlight(entry);
        _pendingRx.Add(entry);
        RxCount++;
        RxByteCount += byteCount;
        AddRecentRxText(entry.Text);
    }

    /// <summary>Queues a TX entry; the UI collection is updated in batches by the flush timer.</summary>
    public void AddTxEntry(LogEntry entry, int byteCount)
    {
        ApplyHighlight(entry);
        _pendingTx.Add(entry);
        TxCount++;
        TxByteCount += byteCount;
    }

    private void AddRecentRxText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_recentRxLock)
        {
            _recentRxTexts.Add(text);
            if (_recentRxTexts.Count > RecentRxTextCap)
                _recentRxTexts.RemoveRange(0, _recentRxTexts.Count - RecentRxTextCap);
        }
    }

    /// <summary>Clears the recent-RX snapshot; call before starting a macro run.</summary>
    public void ClearRecentRxTexts()
    {
        lock (_recentRxLock)
            _recentRxTexts.Clear();
    }

    /// <summary>
    /// Returns the most recent RX text containing <paramref name="pattern"/>
    /// (case-insensitive), or null. Used by MacroManager's WaitFor/Condition,
    /// which polls this from a background task while RX entries accumulate.
    /// </summary>
    public string? FindRecentRxText(string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        lock (_recentRxLock)
        {
            for (int i = _recentRxTexts.Count - 1; i >= 0; i--)
            {
                if (_recentRxTexts[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return _recentRxTexts[i];
            }
        }
        return null;
    }

    private void ApplyHighlight(LogEntry entry)
        => entry.HighlightColor = _highlightService?.GetHighlightColor(entry);

    /// <summary>Moves queued entries into the observable collections (UI thread only).</summary>
    private void FlushPendingEntries()
    {
        if (_pendingRx.Count > 0)
        {
            RxEntries.AddRange(_pendingRx);
            _pendingRx.Clear();
            TrimBuffer(RxEntries);
        }
        if (_pendingTx.Count > 0)
        {
            TxEntries.AddRange(_pendingTx);
            _pendingTx.Clear();
            TrimBuffer(TxEntries);
        }
    }

    /// <summary>
    /// Trims overflow beyond MaxEntries in chunks so the Remove notification
    /// fires once per chunk instead of once per entry.
    /// </summary>
    private void TrimBuffer(ObservableRangeCollection<LogEntry> entries)
    {
        var overflow = entries.Count - MaxEntries;
        if (overflow <= 0) return;
        var removeCount = Math.Min(((overflow + TrimChunkSize - 1) / TrimChunkSize) * TrimChunkSize, entries.Count);
        entries.RemoveRange(0, removeCount);
    }

    public void RecordTxBytes(int byteCount)
    {
        TxByteCount += byteCount;
        _stats?.RecordTx(byteCount);
    }

    public async Task RunParserAsync(LogEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        try
        {
            var data = HexHelper.HexStringToBytes(entry.RawHex);
            var fields = await _parserManager.Engine.ExecuteAsync(data, entry.Timestamp).ConfigureAwait(false);
            if (fields != null && fields.Count > 0)
            {
                entry.Fields = fields;
                if (HexHelper.HasErrorSeverity(fields))
                    ErrorFrameCount++;
            }
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.ParserExecError"], ex.Message)); }
    }

    public void SendData()
    {
        if (string.IsNullOrEmpty(SendText)) return;
        if (IsHexSend && !_hexValidation.IsValid)
        {
            _setStatus(LanguageManager.Instance["Status.HexInvalid"] + ": " + HexValidationError);
            return;
        }
        // Trim leading/trailing whitespace so a stray space-bar press doesn't
        // send a meaningless payload (or, in HEX mode, leave trailing spaces
        // that the user almost certainly didn't mean to transmit).
        var trimmed = IsHexSend ? SendText.TrimEnd() : SendText.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        var toSend = IsHexSend ? trimmed : ExpandVariables(trimmed);

        bool sent;
        if (_networkBridge.IsConnected)
            sent = _networkBridge.Send(toSend, IsHexSend);
        else
            sent = _serial.Send(toSend, IsHexSend);

        if (sent)
        {
            var sentBytes = System.Text.Encoding.UTF8.GetByteCount(toSend);
            if (IsHexSend)
            {
                try { sentBytes = HexHelper.HexStringToBytes(toSend).Length; }
                catch { /* validate above would have caught; fall back to utf8 length */ }
            }
            RecordSendHistory(SendText);
            // Mirror the manual-send bytes into DataStatistics so the TX throughput
            // indicator in the status bar reflects user activity (not just parser-
            // driven loopback traffic).
            _stats?.RecordTx(sentBytes);
            _setStatus(string.Format(LanguageManager.Instance["Status.Sent"], sentBytes));
        }
    }

    private void RecordSendHistory(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Dedupe: move an existing entry to the end rather than creating a duplicate.
        var existing = _sendHistory.IndexOf(text);
        if (existing >= 0) _sendHistory.RemoveAt(existing);
        _sendHistory.Add(text);

        var cap = Math.Max(1, _settings?.MaxSendHistory ?? 50);
        while (_sendHistory.Count > cap)
        {
            var dropped = _sendHistory[0];
            _sendHistory.RemoveAt(0);
            if (SendHistory.Count > 0 && SendHistory[0] == dropped) SendHistory.RemoveAt(0);
        }

        // Mirror the in-memory list into the observable collection for UI binding.
        // This is a small bounded list (cap=50), so a full re-sync is cheap and
        // simpler than tracking incremental move-to-end semantics.
        SendHistory.Clear();
        foreach (var item in _sendHistory) SendHistory.Add(item);

        _historyIndex = _sendHistory.Count;
        PersistSendHistory();
    }

    /// <summary>Snapshot the in-memory history back to <see cref="AppSettings"/> so the next launch can load it.</summary>
    public void PersistSendHistory()
    {
        if (_settings == null) return;
        _settings.SendHistory = new List<string>(_sendHistory);
    }

    public void NavigateHistory(int direction)
    {
        // Legacy wrapper used by MainViewModel; defers to the Try* variant and applies
        // the result via the SendText setter. The XAML code-behind uses TryNavigateHistory
        // directly so it can place the caret at the end of the restored text.
        if (TryNavigateHistory(direction, out var text, out _))
        {
            SendText = text ?? "";
        }
    }

    /// <summary>
    /// Resolves the text the Up/Down history key should load without mutating
    /// <see cref="SendText"/>. Returns false when there is no history to navigate.
    /// On a true return, <paramref name="caretIndex"/> is the position the view
    /// should place the caret at (end of restored text, mirroring shell behaviour
    /// so users can immediately press Enter to re-send).
    /// </summary>
    public bool TryNavigateHistory(int direction, out string? text, out int caretIndex)
    {
        if (_sendHistory.Count == 0)
        {
            text = null;
            caretIndex = 0;
            return false;
        }
        _historyIndex += direction;
        if (_historyIndex < 0) _historyIndex = 0;
        if (_historyIndex >= _sendHistory.Count) _historyIndex = _sendHistory.Count;
        if (_historyIndex < _sendHistory.Count)
        {
            text = _sendHistory[_historyIndex];
            caretIndex = text.Length;
        }
        else
        {
            // Past the newest entry: return to "draft" state.
            text = "";
            caretIndex = 0;
        }
        return true;
    }

    public string ExpandVariables(string input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("{{")) return input;
        var now = DateTime.Now;
        return input
            .Replace("{{timestamp}}", now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Replace("{{date}}", now.ToString("yyyy-MM-dd"))
            .Replace("{{time}}", now.ToString("HH:mm:ss"))
            .Replace("{{counter}}", (++_sendCounter).ToString())
            .Replace("{{ticks}}", now.Ticks.ToString());
    }

    private void SaveToFile(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            _fileExportService.ExportToText(entries, dialog.FileName);
    }

    private void SaveToJson(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            _fileExportService.ExportToJson(entries, dialog.FileName);
    }

    private void SaveToCsv(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            FileExportService.ExportToCsv(entries, dialog.FileName);
    }

    /// <summary>Exports entries to a Wireshark-readable .pcap file. Each packet
    /// carries a direction prefix byte (0x01 TX / 0x02 RX) so the capture can be
    /// split back out later.</summary>
    private void SaveToPcap(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.pcap",
            Filter = "PCAP files (*.pcap)|*.pcap|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _pcapExportService.ExportToPcap(entries, dialog.FileName);
            _setStatus(string.Format(LanguageManager.Instance["Status.PcapExported"], entries.Count, Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
        }
    }

    private void OpenParserDir()
    {
        var dir = _parserManager.GetParserDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void OpenDiffWindow()
    {
        if (SelectedEntry != null && !string.IsNullOrEmpty(SelectedEntry.RawHex))
        {
            var opposite = SelectedEntry.Direction == "RX"
                ? TxEntries.LastOrDefault(e => e.Id != SelectedEntry.Id)
                : RxEntries.LastOrDefault(e => e.Id != SelectedEntry.Id);

            if (opposite != null && !string.IsNullOrEmpty(opposite.RawHex))
            {
                new DiffWindow(SelectedEntry.RawHex, opposite.RawHex).Show();
                _setStatus(string.Format(LanguageManager.Instance["Status.DiffOpened"], SelectedEntry.Id, opposite.Id));
                return;
            }
        }
        new DiffWindow().Show();
        _setStatus(LanguageManager.Instance["Status.DiffWindowOpened"]);
    }

    // Bounded regex cache: user-typed patterns would otherwise grow without limit.
    private const int MaxRegexCacheEntries = 16;
    private static readonly object _regexCacheLock = new();
    private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> _regexCache = new(StringComparer.Ordinal);

    private static System.Text.RegularExpressions.Regex GetOrAddRegex(string pattern)
    {
        lock (_regexCacheLock)
        {
            if (_regexCache.TryGetValue(pattern, out var regex)) return regex;
            regex = new System.Text.RegularExpressions.Regex(pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
            if (_regexCache.Count >= MaxRegexCacheEntries)
            {
                // Drop an arbitrary old entry (first key) to stay bounded.
                var oldest = System.Linq.Enumerable.First(_regexCache.Keys);
                _regexCache.Remove(oldest);
            }
            _regexCache[pattern] = regex;
            return regex;
        }
    }

    private static bool FilterEntry(LogEntry entry, string filter, bool useRegex, bool showDirection, PacketFilterEngine? expressionEngine)
    {
        if (!showDirection) return false;
        if (expressionEngine != null)
        {
            // Expression filter mode: PacketFilter syntax handles everything,
            // so the plain-text/regex path below is bypassed.
            var exprMatch = expressionEngine.Matches(entry);
            entry.IsSearchMatch = exprMatch;
            return exprMatch;
        }
        if (string.IsNullOrWhiteSpace(filter))
        {
            entry.IsSearchMatch = false;
            return true;
        }
        var text = entry.Text ?? "";
        var hex = entry.RawHex ?? "";
        bool matches;
        if (useRegex)
        {
            try
            {
                var regex = GetOrAddRegex(filter);
                matches = regex.IsMatch(text) || regex.IsMatch(hex);
            }
            catch (Exception regexEx) { Debug.WriteLine($"Regex filter error: {regexEx.Message}"); matches = false; }
        }
        else
        {
            matches = text.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase)
                || hex.AsSpan().Contains(filter.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
        entry.IsSearchMatch = matches;
        return matches;
    }

    public string GetFormattedCopyText(IEnumerable<LogEntry> entries, string direction)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var entry in entries)
        {
            var hex = entry.RawHex ?? "";
            var text = entry.Text ?? "";
            var time = entry.Timestamp.ToString("HH:mm:ss.fff");
            if (!string.IsNullOrEmpty(hex))
                sb.AppendLine($"[{time}][{direction}][HEX] {hex}");
            if (!string.IsNullOrEmpty(text))
                sb.AppendLine($"[{time}][{direction}][TXT] {text}");
        }
        return sb.ToString();
    }

    private void LoadParserFingerprints()
    {
        _autoMatcher.Clear();
        foreach (var parserName in _parserManager.AvailableParsers)
        {
            if (parserName == ParserManager.NoParserName)
                continue;

            var fingerprint = _parserManager.GetFingerprint(parserName);
            if (fingerprint != null)
                _autoMatcher.UpdateFingerprint(parserName, fingerprint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flushTimer?.Stop();
        _filterDebounce?.Stop();
        _frameAssembler.OnFrameAssembled -= OnAssembledFrame;
        _frameBuffer.OnFrameAssembled -= _frameBufferFrameHandler;
        _frameBuffer.OnError -= _frameBufferErrorHandler;
        _parserManager.OnParserReloaded -= _parserReloadedHandler;
        _frameAssembler.Dispose();
        _frameBuffer.Dispose();
    }
}
