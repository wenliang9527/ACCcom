using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private readonly FrameAssemblerConfig _frameAssemblerConfig;
    // volatile so the receiver thread's FeedFrameBuffer reads the newest instance
    // after ApplyFrameConfig swaps it; a stale read falls back to the old
    // (already-unsubscribed) buffer, whose Write becomes a no-op once disposed.
    private volatile FrameBuffer _frameBuffer = null!;
    private readonly AutoParserMatcher _autoMatcher;
    private readonly DataStatistics _stats;
    private readonly FileExportService _fileExportService;
    private readonly PcapExportService _pcapExportService = new();
    private readonly Action<string> _setStatus;
    private readonly AppSettings? _settings;
    private readonly HighlightService? _highlightService;
    private readonly DispatcherTimer? _filterDebounce;

    private readonly SendHistoryBuffer _sendHistory;
    private readonly VariableExpander _variableExpander;

    // Batched UI updates: serial/network events arrive on background threads and
    // at high rates; entries are queued here (background-safe, lock-protected)
    // and flushed to the observable collections in one ranged Add per tick
    // (see FlushPendingEntries).
    private const int TrimChunkSize = 100;
    private readonly object _pendingLock = new();
    // Parallel lists (entries + byte counts) instead of a tuple list: the flush
    // hands the entries list straight to AddRange (zero copy) and swaps the pair
    // out under the lock, so a 30ms tick allocates no per-tick list/array.
    private List<LogEntry> _pendingRx = new();
    private List<int> _pendingRxBytes = new();
    private int _pendingRxBytesTotal;
    private List<LogEntry> _pendingTx = new();
    private List<int> _pendingTxBytes = new();
    private int _pendingTxBytesTotal;

    /// <summary>Raised once per flush tick after all pending entries are
    /// appended, so consumers (e.g. auto-scroll) run once instead of once per
    /// item.</summary>
    public event Action? BatchFlushed;

    // Recent RX text used by the macro engine's WaitFor/Condition matching.
    // The buffer is lock-protected internally so the macro runner (background
    // polling) can read safely while RX entries keep arriving; cleared when a
    // macro starts so waits only see data received during that run.
    private readonly RecentRxTextBuffer _recentRxTexts;
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

    private string DescribeHexError(HexHelper.HexValidationResult r)
    {
        if (r.InvalidIndex >= 0) return string.Format(LanguageManager.Instance["HexValidation.InvalidChar"], r.InvalidIndex + 1);
        return LanguageManager.Instance["HexValidation.OddLength"];
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
        _frameAssemblerConfig = frameAssemblerConfig;
        _stats = stats;
        _fileExportService = fileExportService;
        _setStatus = setStatus;
        _settings = settings;

        _autoMatcher = new AutoParserMatcher();
        LoadParserFingerprints();
        _parserReloadedHandler = _ => LoadParserFingerprints();
        _parserManager.OnParserReloaded += _parserReloadedHandler;

        _sendHistory = new SendHistoryBuffer(_settings?.MaxSendHistory ?? 50);
        _variableExpander = new VariableExpander();
        _recentRxTexts = new RecentRxTextBuffer(cap: 512, trimChunk: 128);

        // FrameBuffer is the single frame-assembly path: its config is mapped
        // from the user-facing FrameAssemblerConfig so header/length-field/
        // timeout/max-size edits keep applying, and it is gated by the same
        // Enabled switch (see OnSerialData).
        _frameBufferFrameHandler = OnFrameReady;
        _frameBufferErrorHandler = msg => _setStatus(msg);
        RebuildFrameBuffer();

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
        ClearRxCommand = new RelayCommand(_ =>
        {
            if (!ConfirmClear(LanguageManager.Instance["Confirm.ClearRx"])) return;
            FlushPendingEntries(); RxEntries.Clear(); RxCount = 0; RxByteCount = 0;
        });
        ClearTxCommand = new RelayCommand(_ =>
        {
            if (!ConfirmClear(LanguageManager.Instance["Confirm.ClearTx"])) return;
            FlushPendingEntries(); TxEntries.Clear(); TxCount = 0; TxByteCount = 0;
        });
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

        // Hydrate persistent send history into the in-memory buffer and the UI collection.
        if (_settings?.SendHistory is { Count: > 0 })
        {
            foreach (var item in _settings.SendHistory)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                _sendHistory.Add(item);
                SendHistory.Add(item);
            }
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
                entry.PortTag = LogEntry.MainPortTag;

            // Frame assembly enabled: route RX bytes through the FrameBuffer.
            // Assembled frames surface via OnFrameReady with the full pipeline
            // (HTTP/trigger/logger/stats/display) exactly once; non-frame bytes
            // are held for the frame window like the legacy assembler did. TX
            // entries always bypass assembly and go straight to the display.
            if (entry.Direction == "RX" && _frameAssemblerConfig.Enabled)
            {
                FeedFrameBuffer(entry);
                return;
            }

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            // Serial/network events arrive on a background thread. Enqueue is
            // lock-protected and statistics are atomics/ring buffers, so no UI
            // marshaling is needed here; the 30ms flush timer batches the rest
            // (highlight, counters, callbacks) on the UI thread.
            if (entry.Direction == "RX")
            {
                _stats.RecordRx(byteCount);
                AddRxEntry(entry, byteCount);
            }
            else
            {
                AddTxEntry(entry, byteCount);
            }
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingData"], ex.Message));
        }
    }

    private void FeedFrameBuffer(LogEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        try
        {
            // Single hex pass into a pooled buffer: FrameBuffer.Write copies
            // synchronously into its ring, so the array can be returned
            // immediately. Avoids a byte[] allocation per packet.
            var buffer = ArrayPool<byte>.Shared.Rent(entry.RawHex.Length / 2 + 1);
            try
            {
                var byteCount = HexHelper.HexStringToBytes(entry.RawHex, buffer);
                if (byteCount > 0)
                    _frameBuffer.Write(buffer, 0, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch { }
    }

    /// <summary>(Re)builds the FrameBuffer from the current FrameAssemblerConfig.
    /// Called from the constructor and again when the user changes frame-assembly
    /// settings so header/length-field edits take effect immediately.</summary>
    public void ApplyFrameConfig()
    {
        RebuildFrameBuffer();
    }

    private void RebuildFrameBuffer()
    {
        // Construct + subscribe the new buffer first, then swap the field, then
        // dispose the old one. This closes the window where writes land on a
        // buffer whose OnFrameAssembled is not (yet) subscribed, which would
        // silently drop assembled frames on every config change.
        var next = new FrameBuffer(_frameAssemblerConfig.ToFrameBufferConfig(), _autoMatcher, _parserManager);
        next.OnFrameAssembled += _frameBufferFrameHandler;
        next.OnError += _frameBufferErrorHandler;

        var old = _frameBuffer;
        _frameBuffer = next;

        if (old != null)
        {
            old.OnFrameAssembled -= _frameBufferFrameHandler;
            old.OnError -= _frameBufferErrorHandler;
            old.Dispose();
        }
    }

    private void OnFrameReady(LogEntry entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry.PortTag))
                entry.PortTag = LogEntry.MainPortTag;

            _http.AddEntry(entry);
            _triggerService.Evaluate(entry);

            int byteCount = 0;
            if (!string.IsNullOrEmpty(entry.RawHex))
                byteCount = HexHelper.CountHexBytes(entry.RawHex);

            // Frame-buffer callbacks arrive on the receive thread; enqueue is
            // lock-protected and the logger is internally synchronized, so no UI
            // marshaling is needed here either.
            _logger.Write(entry);

            if (entry.Direction == "RX")
            {
                _stats.RecordRx(byteCount);
                if (HexHelper.HasErrorSeverity(entry.Fields))
                    _stats.RecordError();
                AddRxEntry(entry, byteCount);
            }
            else
            {
                AddTxEntry(entry, byteCount);
            }
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ErrorProcessingFrame"], ex.Message));
        }
    }

    /// <summary>
    /// Queues an RX entry from the receiving thread (lock-protected). Highlight
    /// matching and observable updates are deferred to the UI-thread flush timer.
    /// </summary>
    /// <summary>Queues an RX entry from the receiving thread (lock-protected).
    /// Highlight is computed here (enqueue thread) — before the entry enters the
    /// pending list — so the UI flush never scans rules per frame; the lock
    /// happens-before guarantees the value is visible when the UI reads it.</summary>
    public void AddRxEntry(LogEntry entry, int byteCount)
    {
        ApplyHighlight(entry);
        AddRecentRxText(entry.Text);
        lock (_pendingLock)
        {
            _pendingRx.Add(entry);
            _pendingRxBytes.Add(byteCount);
            _pendingRxBytesTotal += byteCount;
        }
    }

    /// <summary>Queues a TX entry from the receiving thread (lock-protected).
    /// Highlight is computed here for the same reason as RX.</summary>
    public void AddTxEntry(LogEntry entry, int byteCount)
    {
        ApplyHighlight(entry);
        lock (_pendingLock)
        {
            _pendingTx.Add(entry);
            _pendingTxBytes.Add(byteCount);
            _pendingTxBytesTotal += byteCount;
        }
    }

    private void AddRecentRxText(string? text)
        => _recentRxTexts.Add(text);

    /// <summary>Clears the recent-RX snapshot; call before starting a macro run.</summary>
    public void ClearRecentRxTexts()
        => _recentRxTexts.Clear();

    /// <summary>
    /// Returns the most recent RX text containing <paramref name="pattern"/>
    /// (case-insensitive), or null. Used by MacroManager's WaitFor/Condition,
    /// which polls this from a background task while RX entries accumulate.
    /// </summary>
    public string? FindRecentRxText(string pattern)
        => _recentRxTexts.FindLatestContaining(pattern);

    private void ApplyHighlight(LogEntry entry)
        => entry.HighlightColor = _highlightService?.GetHighlightColor(entry);

    /// <summary>Moves queued entries into the observable collections (UI thread only).
    /// Highlight is applied once per batch so per-packet UI updates never trigger
    /// a binding storm. The pending lists are swap-exchanged under the lock (old
    /// lists out, fresh lists in) so no per-tick snapshot copy is needed, and the
    /// entries list is handed to AddRange directly (List is IList, hits the bulk
    /// path with zero copy). Counters accumulate silently here; the 1Hz stats tick
    /// raises their PropertyChanged via <see cref="NotifyCountsChanged"/>.</summary>
    private void FlushPendingEntries()
    {
        List<LogEntry>? rxBatch = null;
        List<int>? rxBytes = null;
        int rxBytesTotal = 0;
        List<LogEntry>? txBatch = null;
        List<int>? txBytes = null;
        int txBytesTotal = 0;
        lock (_pendingLock)
        {
            if (_pendingRx.Count > 0)
            {
                rxBatch = _pendingRx;
                rxBytes = _pendingRxBytes;
                rxBytesTotal = _pendingRxBytesTotal;
                _pendingRx = new List<LogEntry>();
                _pendingRxBytes = new List<int>();
                _pendingRxBytesTotal = 0;
            }
            if (_pendingTx.Count > 0)
            {
                txBatch = _pendingTx;
                txBytes = _pendingTxBytes;
                txBytesTotal = _pendingTxBytesTotal;
                _pendingTx = new List<LogEntry>();
                _pendingTxBytes = new List<int>();
                _pendingTxBytesTotal = 0;
            }
        }

        if (rxBatch != null)
        {
            RxEntries.AddRange(rxBatch);
            _rxCount += rxBatch.Count;
            _rxByteCount += rxBytesTotal;
            for (int i = 0; i < rxBatch.Count; i++)
            {
                var entry = rxBatch[i];
                OnRxProcessed?.Invoke(entry);
                OnEntryProcessed?.Invoke(entry, rxBytes![i]);
            }
            TrimBuffer(RxEntries);
        }
        if (txBatch != null)
        {
            TxEntries.AddRange(txBatch);
            _txCount += txBatch.Count;
            _txByteCount += txBytesTotal;
            for (int i = 0; i < txBatch.Count; i++)
                OnEntryProcessed?.Invoke(txBatch[i], txBytes![i]);
            TrimBuffer(TxEntries);
        }
        BatchFlushed?.Invoke();
    }

    /// <summary>Raises PropertyChanged for the counter properties. Called once per
    /// second by the stats tick (counters accumulate silently during flush) so the
    /// status bar bindings update at 1Hz instead of 33Hz.</summary>
    public void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(RxCount));
        OnPropertyChanged(nameof(RxByteCount));
        OnPropertyChanged(nameof(TxCount));
        OnPropertyChanged(nameof(TxByteCount));
        OnPropertyChanged(nameof(ErrorFrameCount));
    }

    /// <summary>
    /// Trims overflow beyond MaxEntries in chunks so the Remove notification
    /// fires once per chunk instead of once per entry.
    /// </summary>
    /// <summary>Yes/No guard for destructive actions (clear RX/TX buffers).
    /// Uses the same MessageBox pattern as ShortcutViewModel page deletion.</summary>
    private static bool ConfirmClear(string message)
        => System.Windows.MessageBox.Show(message,
            LanguageManager.Instance["Confirm.Title"],
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    private void TrimBuffer(ObservableRangeCollection<LogEntry> entries)
    {
        // Chunk-rounding so RemoveRange fires one notification per chunk instead
        // of one per entry (see EntryListTrimmer for the exact semantics).
        var removeCount = EntryListTrimmer.ComputeRemoveCount(entries.Count, MaxEntries, TrimChunkSize);
        if (removeCount > 0)
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
        // Dedupe + capacity eviction live in SendHistoryBuffer; mirror the entry
        // into the observable collection for UI binding. This is a small bounded
        // list (cap=50), so a full re-sync is cheap and simpler than tracking
        // incremental move-to-end semantics.
        _sendHistory.Add(text);
        SendHistory.Clear();
        foreach (var item in _sendHistory.Entries) SendHistory.Add(item);
        PersistSendHistory();
    }

    /// <summary>Snapshot the in-memory history back to <see cref="AppSettings"/> so the next launch can load it.</summary>
    public void PersistSendHistory()
    {
        if (_settings == null) return;
        _settings.SendHistory = new List<string>(_sendHistory.Entries);
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
    /// so users can immediately press Enter to re-send). Navigation starts at the
    /// newest entry and clamps at both ends, with a "draft" slot past the newest.
    /// </summary>
    public bool TryNavigateHistory(int direction, out string? text, out int caretIndex)
        => _sendHistory.TryNavigate(direction, out text, out caretIndex);

    public string ExpandVariables(string input)
        => _variableExpander.Expand(input);

    private void SaveToFile(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = TimestampedFileName.Build("ACCCOM", DateTime.Now, tag, "txt"),
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
            FileName = TimestampedFileName.Build("ACCCOM", DateTime.Now, tag, "json"),
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
            FileName = TimestampedFileName.Build("ACCCOM", DateTime.Now, tag, "csv"),
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
            FileName = TimestampedFileName.Build("ACCCOM", DateTime.Now, tag, "pcap"),
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

    private static bool FilterEntry(LogEntry entry, string filter, bool useRegex, bool showDirection, PacketFilterEngine? expressionEngine)
        => DataPanelFilter.FilterEntry(entry, filter, useRegex, showDirection, expressionEngine,
            regexError => Debug.WriteLine($"Regex filter error: {regexError}"));

    public string GetFormattedCopyText(IEnumerable<LogEntry> entries, string direction)
        => EntryTextFormatter.Format(entries, direction);

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
        _frameBuffer.OnFrameAssembled -= _frameBufferFrameHandler;
        _frameBuffer.OnError -= _frameBufferErrorHandler;
        _parserManager.OnParserReloaded -= _parserReloadedHandler;
        _frameBuffer.Dispose();
    }
}
