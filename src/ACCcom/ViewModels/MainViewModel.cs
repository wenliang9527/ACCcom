using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Timers;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SerialService _serial = new();
    private readonly LoggerService _logger = new();
    private readonly HttpService _http;
    private readonly ParserManager _parserManager;
    private bool _disposed;

    // --- 串口配置 ---
    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<int> BaudRates { get; } = new() { 300, 600, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    public ObservableCollection<int> DataBitsList { get; } = new() { 5, 6, 7, 8 };
    public ObservableCollection<string> StopBitsList { get; } = new() { "None", "One", "Two" };
    public ObservableCollection<string> ParityList { get; } = new() { "None", "Odd", "Even" };

    private string _selectedPort = "";
    public string SelectedPort { get => _selectedPort; set => SetField(ref _selectedPort, value); }

    private int _selectedBaudRate = 115200;
    public int SelectedBaudRate { get => _selectedBaudRate; set => SetField(ref _selectedBaudRate, value); }

    private int _selectedDataBits = 8;
    public int SelectedDataBits { get => _selectedDataBits; set => SetField(ref _selectedDataBits, value); }

    private int _selectedStopBits = 1;
    public int SelectedStopBits { get => _selectedStopBits; set => SetField(ref _selectedStopBits, value); }

    private int _selectedParity = 0;
    public int SelectedParity { get => _selectedParity; set => SetField(ref _selectedParity, value); }

    private bool _dtrEnable;
    public bool DtrEnable { get => _dtrEnable; set => SetField(ref _dtrEnable, value); }

    private bool _rtsEnable;
    public bool RtsEnable { get => _rtsEnable; set => SetField(ref _rtsEnable, value); }

    // --- 收发显示 ---
    public ObservableCollection<LogEntry> RxEntries { get; } = new();
    public ObservableCollection<LogEntry> TxEntries { get; } = new();
    public ObservableCollection<string> ShortcutCommands { get; } = new() { "AT+GMR", "AT+RST", "AT+CGATT?" };

    private string _sendText = "";
    public string SendText { get => _sendText; set => SetField(ref _sendText, value); }

    private bool _isHexSend;
    public bool IsHexSend { get => _isHexSend; set => SetField(ref _isHexSend, value); }

    private bool _isHexDisplayRx;
    public bool IsHexDisplayRx { get => _isHexDisplayRx; set => SetField(ref _isHexDisplayRx, value); }

    private bool _isHexDisplayTx;
    public bool IsHexDisplayTx { get => _isHexDisplayTx; set => SetField(ref _isHexDisplayTx, value); }

    private bool _enableRxTimestamp = true;
    public bool EnableRxTimestamp { get => _enableRxTimestamp; set => SetField(ref _enableRxTimestamp, value); }

    private bool _enableTxTimestamp = true;
    public bool EnableTxTimestamp { get => _enableTxTimestamp; set => SetField(ref _enableTxTimestamp, value); }

    private string _statusText = "就绪";
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

    private int _rxCount;
    public int RxCount { get => _rxCount; set => SetField(ref _rxCount, value); }

    private int _txCount;
    public int TxCount { get => _txCount; set => SetField(ref _txCount, value); }

    private bool _isOpen;
    public bool IsOpen { get => _isOpen; set => SetField(ref _isOpen, value); }

    private string _httpUrl = "http://127.0.0.1:8899";
    public string HttpUrl { get => _httpUrl; set => SetField(ref _httpUrl, value); }

    // --- 协议解析 ---
    private string _selectedParser = "(无)";
    public string SelectedParser
    {
        get => _selectedParser;
        set
        {
            if (SetField(ref _selectedParser, value))
            {
                if (!_parserManager.Activate(value))
                    StatusText = $"解析器加载失败: {_parserManager.LastError}";
                else if (value != "(无)")
                    StatusText = $"解析器: {value}";
            }
        }
    }

    public ObservableCollection<string> AvailableParsers => _parserManager.AvailableParsers;

    private LogEntry? _selectedEntry;
    public LogEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetField(ref _selectedEntry, value);
    }

    // --- 循环发送 ---
    private System.Timers.Timer? _loopTimer;
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

    // --- 命令 ---
    public ICommand OpenCloseCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand SendShortcutCommand { get; }
    public ICommand AddShortcutCommand { get; }
    public ICommand ClearRxCommand { get; }
    public ICommand ClearTxCommand { get; }
    public ICommand SaveRxCommand { get; }
    public ICommand SaveTxCommand { get; }
    public ICommand StopLoopCommand { get; }
    public ICommand OpenParserDirCommand { get; }

    public MainViewModel()
    {
        _parserManager = new ParserManager(dispatch: action => System.Windows.Application.Current.Dispatcher.Invoke(action));

        OpenCloseCommand = new RelayCommand(_ => ToggleOpenClose());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        SendCommand = new RelayCommand(_ => SendData());
        SendShortcutCommand = new RelayCommand(p => SendShortcut(p?.ToString() ?? ""));
        AddShortcutCommand = new RelayCommand(_ => AddShortcut());
        ClearRxCommand = new RelayCommand(_ => RxEntries.Clear());
        ClearTxCommand = new RelayCommand(_ => TxEntries.Clear());
        SaveRxCommand = new RelayCommand(_ => SaveToFile(RxEntries, "RX"));
        SaveTxCommand = new RelayCommand(_ => SaveToFile(TxEntries, "TX"));
        StopLoopCommand = new RelayCommand(_ => StopLoop());
        OpenParserDirCommand = new RelayCommand(_ => OpenParserDir());

        _serial.OnDataReceived += OnSerialData;
        _serial.OnError += msg => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _serial.OnDisconnected += () => System.Windows.Application.Current.Dispatcher.Invoke(() => { IsOpen = false; StatusText = "串口已断开"; });

        _http = new HttpService(_serial, _parserManager);
        _http.Start();
        HttpUrl = "http://127.0.0.1:8899";

        RefreshPorts();
    }

    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var p in SerialService.GetAvailablePorts())
            AvailablePorts.Add(p);
    }

    private void ToggleOpenClose()
    {
        if (IsOpen)
        {
            _serial.Close();
            IsOpen = false;
            StatusText = "串口已关闭";
        }
        else
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                StatusText = "请选择串口";
                return;
            }
            var config = new SerialConfig
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                DataBits = SelectedDataBits,
                StopBits = SelectedStopBits,
                Parity = SelectedParity,
                DtrEnable = DtrEnable,
                RtsEnable = RtsEnable
            };
            _serial.Open(config);
            IsOpen = _serial.IsOpen;
            StatusText = IsOpen ? $"已连接 {SelectedPort} | {SelectedBaudRate} bps" : "打开失败";
        }
    }

    private void SendData()
    {
        if (string.IsNullOrEmpty(SendText)) return;
        if (_serial.Send(SendText, IsHexSend))
        {
            TxCount++;
            SendText = "";
        }
    }

    private void SendShortcut(string cmd)
    {
        if (_serial.Send(cmd, false))
            TxCount++;
    }

    private void AddShortcut()
    {
        ShortcutCommands.Add("AT+新指令");
    }

    private void StartLoop()
    {
        if (!IsLoopSend || string.IsNullOrEmpty(SendText)) return;
        StopLoop();
        _loopTimer = new System.Timers.Timer(LoopInterval > 0 ? LoopInterval : 1000);
        _loopTimer.Elapsed += (_, _) =>
        {
            if (IsOpen) _serial.Send(SendText, IsHexSend);
        };
        _loopTimer.Start();
        IsLooping = true;
        StatusText = "循环发送中...";
    }

    private void StopLoop()
    {
        _loopTimer?.Stop();
        _loopTimer?.Dispose();
        _loopTimer = null;
        IsLooping = false;
        IsLoopSend = false;
        StatusText = "循环已停止";
    }

    private void OpenParserDir()
    {
        var dir = _parserManager.GetParserDir();
        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private void OnSerialData(LogEntry entry)
    {
        // Add to HTTP API buffer (thread-safe, no dispatcher needed)
        _http.AddEntry(entry);

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _logger.Write(entry);

            if (entry.Direction == "RX" && _parserManager.ActiveParserName != null)
                RunParser(entry);

            if (entry.Direction == "RX")
            {
                RxEntries.Add(entry);
                RxCount++;
            }
            else
            {
                TxEntries.Add(entry);
                TxCount++;
            }
        });
    }

    private void RunParser(LogEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RawHex)) return;
        try
        {
            var hex = entry.RawHex.Replace(" ", "");
            var data = Convert.FromHexString(hex);
            var fields = _parserManager.Engine.Execute(data, entry.Timestamp);
            if (fields != null && fields.Count > 0)
                entry.Fields = fields;
        }
        catch { }
    }

    private void SaveToFile(ObservableCollection<LogEntry> entries, string tag)
    {
        if (entries.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"ACCCOM_{tag}_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            using var sw = new StreamWriter(dialog.FileName);
            foreach (var e in entries)
            {
                var ts = e.Timestamp.ToString("HH:mm:ss.fff");
                sw.WriteLine($"[{ts}][{e.Direction}] {e.RawHex} | {e.Text}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopLoop();
        _http.Dispose();
        _parserManager.Dispose();
        _serial.Dispose();
        _logger.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
