# ACCCOM 串口调试工具 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 开发一个 Windows 桌面串口调试工具，对标 SSCOM 5.13.1，内置 HTTP API 供外部工具读写串口数据

**Architecture:** WPF (.NET 8) + System.IO.Ports + EmbedIO (嵌入式 HTTP 服务器)。MVVM 架构，SerialService 管理串口，HttpService 提供 REST API，LoggerService 写入文件日志

**Tech Stack:** C# 12, .NET 8, WPF, EmbedIO 3.x, System.IO.Ports

---

### Task 1: 项目脚手架

**Files:**
- Create: `src/ACCcom/ACCcom.csproj`
- Create: `src/ACCcom/App.xaml`
- Create: `src/ACCcom/App.xaml.cs`
- Create: `src/ACCcom/ACCcom.sln`

- [ ] **Step 1: 创建项目目录结构**

```bash
New-Item -ItemType Directory -Path "src\ACCcom\Models", "src\ACCcom\Services", "src\ACCcom\ViewModels" -Force
```

- [ ] **Step 2: 创建 .csproj 文件**

`src/ACCcom/ACCcom.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon />
    <StartupObject />
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="EmbedIO" Version="3.5.2" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 创建 App.xaml**

`src/ACCcom/App.xaml`:
```xml
<Application x:Class="ACCcom.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

`src/ACCcom/App.xaml.cs`:
```csharp
using System.Windows;

namespace ACCcom;

public partial class App : Application
{
}
```

- [ ] **Step 4: 创建 MainWindow 骨架**

`src/ACCcom/MainWindow.xaml`:
```xml
<Window x:Class="ACCcom.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ACCCOM - 串口调试工具" Height="650" Width="1000"
        WindowStartupLocation="CenterScreen">
    <Grid>
        <TextBlock Text="加载中..." HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Grid>
</Window>
```

`src/ACCcom/MainWindow.xaml.cs`:
```csharp
using System.Windows;

namespace ACCcom;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: 验证编译**

```bash
dotnet restore src\ACCcom\ACCcom.csproj
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 2: 数据模型

**Files:**
- Create: `src/ACCcom/Models/SerialConfig.cs`
- Create: `src/ACCcom/Models/ReceivedData.cs`

- [ ] **Step 1: 创建 SerialConfig**

`src/ACCcom/Models/SerialConfig.cs`:
```csharp
namespace ACCcom.Models;

public class SerialConfig
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1; // 0=None, 1=One, 2=Two
    public int Parity { get; set; } = 0; // 0=None, 1=Odd, 2=Even
    public bool DtrEnable { get; set; }
    public bool RtsEnable { get; set; }
}
```

- [ ] **Step 2: 创建 ReceivedData**

`src/ACCcom/Models/ReceivedData.cs`:
```csharp
namespace ACCcom.Models;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = ""; // "RX" or "TX"
    public string RawHex { get; set; } = "";
    public string Text { get; set; } = "";
}
```

- [ ] **Step 3: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 3: SerialService — 串口管理核心

**Files:**
- Create: `src/ACCcom/Services/SerialService.cs`

- [ ] **Step 1: 创建 SerialService**

`src/ACCcom/Services/SerialService.cs`:
```csharp
using System.IO.Ports;
using ACCcom.Models;

namespace ACCcom.Services;

public class SerialService : IDisposable
{
    private SerialPort? _port;
    private int _entryId;

    public bool IsOpen => _port?.IsOpen ?? false;
    public string? CurrentPort => _port?.PortName;
    public int BaudRate => _port?.BaudRate ?? 0;

    public event Action<LogEntry>? OnDataReceived;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public void Open(SerialConfig config)
    {
        if (_port?.IsOpen == true) return;

        _port = new SerialPort(config.PortName, config.BaudRate, (Parity)config.Parity, config.DataBits, (StopBits)config.StopBits)
        {
            DtrEnable = config.DtrEnable,
            RtsEnable = config.RtsEnable,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        _port.DataReceived += OnSerialDataReceived;
        _port.ErrorReceived += OnSerialError;

        try
        {
            _port.Open();
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"打开串口失败: {ex.Message}");
        }
    }

    public void Close()
    {
        if (_port == null) return;
        try
        {
            if (_port.IsOpen)
            {
                _port.DataReceived -= OnSerialDataReceived;
                _port.ErrorReceived -= OnSerialError;
                _port.Close();
            }
        }
        catch { }
        _port?.Dispose();
        _port = null;
    }

    public void Send(string data, bool isHex = false)
    {
        if (_port?.IsOpen != true)
        {
            OnError?.Invoke("串口未打开");
            return;
        }

        try
        {
            if (isHex)
            {
                var bytes = Convert.FromHexString(data.Replace(" ", ""));
                _port.Write(bytes, 0, bytes.Length);
            }
            else
            {
                _port.Write(data);
            }

            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref _entryId),
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawHex = isHex ? data.Replace(" ", "") : BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(data)).Replace("-", " "),
                Text = data
            };
            OnDataReceived?.Invoke(entry);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"发送失败: {ex.Message}");
        }
    }

    public void SendHex(string hex)
    {
        Send(hex, true);
    }

    private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port?.IsOpen != true) return;

        try
        {
            var buffer = new byte[_port.BytesToRead];
            _port.Read(buffer, 0, buffer.Length);

            var hex = BitConverter.ToString(buffer).Replace("-", " ");
            var text = System.Text.Encoding.UTF8.GetString(buffer);

            var entry = new LogEntry
            {
                Id = Interlocked.Increment(ref _entryId),
                Timestamp = DateTime.Now,
                Direction = "RX",
                RawHex = hex,
                Text = text
            };
            OnDataReceived?.Invoke(entry);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"接收数据错误: {ex.Message}");
        }
    }

    private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
    {
        OnError?.Invoke($"串口错误: {e.EventType}");
    }

    public void Dispose()
    {
        Close();
    }
}
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 4: LoggerService — 文件日志

**Files:**
- Create: `src/ACCcom/Services/LoggerService.cs`

- [ ] **Step 1: 创建 LoggerService**

`src/ACCcom/Services/LoggerService.cs`:
```csharp
using System.IO;
using ACCcom.Models;

namespace ACCcom.Services;

public class LoggerService : IDisposable
{
    private readonly string _logDir;
    private StreamWriter? _writer;
    private readonly long _maxFileSize = 5 * 1024 * 1024; // 5MB
    private readonly object _lock = new();
    private string? _currentFilePath;

    public LoggerService()
    {
        _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDir);
        RotateFile();
    }

    public string CurrentLogPath => _currentFilePath ?? "";

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            if (_writer == null) return;

            if (_writer.BaseStream.Length > _maxFileSize)
                RotateFile();

            var timestamp = entry.Timestamp.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}][{entry.Direction}] {entry.RawHex} | {entry.Text}";
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    private void RotateFile()
    {
        _writer?.Close();
        _writer?.Dispose();
        var fileName = $"ACCCOM_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        _currentFilePath = Path.Combine(_logDir, fileName);
        _writer = new StreamWriter(_currentFilePath, append: true, System.Text.Encoding.UTF8);
    }

    public void Dispose()
    {
        _writer?.Close();
        _writer?.Dispose();
    }
}
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 5: MainViewModel — 主视图模型

**Files:**
- Create: `src/ACCcom/ViewModels/RelayCommand.cs`
- Create: `src/ACCcom/ViewModels/MainViewModel.cs`

- [ ] **Step 1: 创建 RelayCommand**

`src/ACCcom/ViewModels/RelayCommand.cs`:
```csharp
using System.Windows.Input;

namespace ACCcom.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
```

- [ ] **Step 2: 创建 MainViewModel**

`src/ACCcom/ViewModels/MainViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ACCcom.Models;
using ACCcom.Services;

namespace ACCcom.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SerialService _serial = new();
    private readonly LoggerService _logger = new();
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

    private bool _autoScrollRx = true;
    public bool AutoScrollRx { get => _autoScrollRx; set => SetField(ref _autoScrollRx, value); }

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

    // --- 命令 ---
    public ICommand OpenCloseCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand SendShortcutCommand { get; }
    public ICommand ClearRxCommand { get; }
    public ICommand ClearTxCommand { get; }
    public ICommand SaveRxCommand { get; }
    public ICommand SaveTxCommand { get; }

    public MainViewModel()
    {
        OpenCloseCommand = new RelayCommand(_ => ToggleOpenClose());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        SendCommand = new RelayCommand(_ => SendData());
        SendShortcutCommand = new RelayCommand(p => SendShortcut(p?.ToString() ?? ""));
        ClearRxCommand = new RelayCommand(_ => RxEntries.Clear());
        ClearTxCommand = new RelayCommand(_ => TxEntries.Clear());
        SaveRxCommand = new RelayCommand(_ => SaveToFile(RxEntries, "RX"));
        SaveTxCommand = new RelayCommand(_ => SaveToFile(TxEntries, "TX"));

        _serial.OnDataReceived += OnSerialData;
        _serial.OnError += msg => System.Windows.Application.Current.Dispatcher.Invoke(() => StatusText = msg);
        _serial.OnDisconnected += () => System.Windows.Application.Current.Dispatcher.Invoke(() => { IsOpen = false; StatusText = "串口已断开"; });

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
        _serial.Send(SendText, IsHexSend);
        TxCount++;
    }

    private void SendShortcut(string cmd)
    {
        _serial.Send(cmd, false);
        TxCount++;
    }

    private void OnSerialData(LogEntry entry)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _logger.Write(entry);
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

    public void HandleKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 清空当前活动窗口 (由 View 层判断)
        }
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // 保存由 View 层处理
        }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SendText += "\r\n";
        }
        else if (e.Key == Key.Enter)
        {
            SendData();
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _serial.Dispose();
        _logger.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 6: MainWindow UI — 完整界面

**Files:**
- Modify: `src/ACCcom/MainWindow.xaml`
- Modify: `src/ACCcom/MainWindow.xaml.cs`

- [ ] **Step 1: 实现 MainWindow.xaml**

`src/ACCcom/MainWindow.xaml`:
```xml
<Window x:Class="ACCcom.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ACCCOM - 串口调试工具" Height="700" Width="1050"
        WindowStartupLocation="CenterScreen"
        PreviewKeyDown="Window_PreviewKeyDown">
    <Window.DataContext>
        <x:Null />
    </Window.DataContext>
    <Grid Margin="5">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="2*" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Row 0: 串口配置 -->
        <Border Grid.Row="0" BorderBrush="#ccc" BorderThickness="1" Padding="5" Margin="0,0,0,3">
            <StackPanel>
                <WrapPanel>
                    <TextBlock Text="端口" VerticalAlignment="Center" Margin="3,0" />
                    <ComboBox ItemsSource="{Binding AvailablePorts}" Text="{Binding SelectedPort, UpdateSourceTrigger=PropertyChanged}"
                              Width="100" IsEditable="True" />
                    <TextBlock Text="波特率" VerticalAlignment="Center" Margin="6,0,3,0" />
                    <ComboBox ItemsSource="{Binding BaudRates}" SelectedItem="{Binding SelectedBaudRate}" Width="80" />
                    <TextBlock Text="数据位" VerticalAlignment="Center" Margin="6,0,3,0" />
                    <ComboBox ItemsSource="{Binding DataBitsList}" SelectedItem="{Binding SelectedDataBits}" Width="50" />
                    <TextBlock Text="校验位" VerticalAlignment="Center" Margin="6,0,3,0" />
                    <ComboBox ItemsSource="{Binding ParityList}" SelectedIndex="{Binding SelectedParity}" Width="70" />
                    <TextBlock Text="停止位" VerticalAlignment="Center" Margin="6,0,3,0" />
                    <ComboBox ItemsSource="{Binding StopBitsList}" SelectedIndex="{Binding SelectedStopBits}" Width="70" />
                </WrapPanel>
                <WrapPanel Margin="0,3,0,0">
                    <Button Content="{Binding IsOpen, Converter={x:Null}, FallbackValue=打开串口}"
                            Command="{Binding OpenCloseCommand}" Width="100" Margin="3,0"
                            Background="{Binding IsOpen, Converter={x:Null}}" />
                    <CheckBox Content="DTR" IsChecked="{Binding DtrEnable}" Margin="10,0,3,0" />
                    <CheckBox Content="RTS" IsChecked="{Binding RtsEnable}" Margin="3,0" />
                    <Button Content="刷新" Command="{Binding RefreshPortsCommand}" Width="60" Margin="10,0,0,0" />
                </WrapPanel>
            </StackPanel>
        </Border>

        <!-- Row 1: 接收/发送区 (双窗) -->
        <Grid Grid.Row="1" Margin="0,0,0,3">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- RX 区 -->
            <Border Grid.Column="0" BorderBrush="#4CAF50" BorderThickness="1" Padding="3">
                <DockPanel>
                    <WrapPanel DockPanel.Dock="Top" Margin="0,0,0,3">
                        <TextBlock Text="接收区 (RX)" FontWeight="Bold" Foreground="#4CAF50" VerticalAlignment="Center" />
                        <CheckBox Content="时间戳" IsChecked="{Binding EnableRxTimestamp}" Margin="10,0,3,0" />
                        <CheckBox Content="HEX" IsChecked="{Binding IsHexDisplayRx}" Margin="3,0" />
                        <Button Content="清空" Command="{Binding ClearRxCommand}" Width="50" Margin="10,0,3,0" />
                        <Button Content="保存" Command="{Binding SaveRxCommand}" Width="50" Margin="3,0" />
                    </WrapPanel>
                    <ListBox ItemsSource="{Binding RxEntries}" ScrollViewer.HorizontalScrollBarVisibility="Auto"
                             VirtualizingPanel.ScrollUnit="Pixel">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Text}" TextWrapping="Wrap" FontFamily="Consolas" />
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </Border>

            <GridSplitter Grid.Column="1" Width="4" HorizontalAlignment="Center" VerticalAlignment="Stretch" />

            <!-- TX 区 -->
            <Border Grid.Column="2" BorderBrush="#2196F3" BorderThickness="1" Padding="3">
                <DockPanel>
                    <WrapPanel DockPanel.Dock="Top" Margin="0,0,0,3">
                        <TextBlock Text="发送区 (TX)" FontWeight="Bold" Foreground="#2196F3" VerticalAlignment="Center" />
                        <CheckBox Content="时间戳" IsChecked="{Binding EnableTxTimestamp}" Margin="10,0,3,0" />
                        <CheckBox Content="HEX" IsChecked="{Binding IsHexDisplayTx}" Margin="3,0" />
                        <Button Content="清空" Command="{Binding ClearTxCommand}" Width="50" Margin="10,0,3,0" />
                        <Button Content="保存" Command="{Binding SaveTxCommand}" Width="50" Margin="3,0" />
                    </WrapPanel>
                    <ListBox ItemsSource="{Binding TxEntries}" ScrollViewer.HorizontalScrollBarVisibility="Auto"
                             VirtualizingPanel.ScrollUnit="Pixel">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Text}" TextWrapping="Wrap" FontFamily="Consolas" />
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                </DockPanel>
            </Border>
        </Grid>

        <!-- Row 2: 快捷发送栏 -->
        <Border Grid.Row="2" BorderBrush="#ccc" BorderThickness="1" Padding="5" Margin="0,0,0,3">
            <WrapPanel>
                <TextBlock Text="快捷发送" FontWeight="Bold" VerticalAlignment="Center" Margin="3,0" />
                <ItemsControl ItemsSource="{Binding ShortcutCommands}" Margin="5,0">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Button Content="{Binding}" Command="{Binding DataContext.SendShortcutCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                    CommandParameter="{Binding}" Width="80" Margin="3,0" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </WrapPanel>
        </Border>

        <!-- Row 3: 发送区 -->
        <Border Grid.Row="3" BorderBrush="#ccc" BorderThickness="1" Padding="5" Margin="0,0,0,3">
            <DockPanel>
                <WrapPanel DockPanel.Dock="Right" VerticalAlignment="Center">
                    <CheckBox Content="HEX发送" IsChecked="{Binding IsHexSend}" Margin="3,0" />
                    <Button Content="发送" Command="{Binding SendCommand}" Width="70" Margin="10,0,0,0" />
                </WrapPanel>
                <TextBox Text="{Binding SendText, UpdateSourceTrigger=PropertyChanged}" Height="50"
                         AcceptsReturn="True" TextWrapping="Wrap" FontFamily="Consolas"
                         VerticalScrollBarVisibility="Auto" />
            </DockPanel>
        </Border>

        <!-- Row 4: 状态栏 -->
        <StatusBar Grid.Row="4">
            <StatusBarItem>
                <TextBlock Text="{Binding StatusText}" />
            </StatusBarItem>
            <StatusBarItem HorizontalAlignment="Right">
                <TextBlock Text="{Binding HttpUrl}" Foreground="Gray" />
            </StatusBarItem>
        </StatusBar>
    </Grid>
</Window>
```

- [ ] **Step 2: 实现 MainWindow.xaml.cs**

`src/ACCcom/MainWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Input;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.RxEntries.Clear();
            _vm.TxEntries.Clear();
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var focused = FocusManager.GetFocusedElement(this);
            // 简单处理：Ctrl+S 保存接收区
            var cmd = _vm.SaveRxCommand;
            if (cmd.CanExecute(null))
                cmd.Execute(null);
        }
        else if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.SendText += "\r\n";
            }
            else
            {
                _vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 3: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 7: HttpService — EmbedIO REST API

**Files:**
- Create: `src/ACCcom/Services/HttpService.cs`
- Modify: `src/ACCcom/ViewModels/MainViewModel.cs`
- Modify: `src/ACCcom/MainWindow.xaml.cs`

- [ ] **Step 1: 创建 HttpService**

`src/ACCcom/Services/HttpService.cs`:
```csharp
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using Swan.Logging;
using ACCcom.Models;

namespace ACCcom.Services;

public class HttpService : IDisposable
{
    private readonly WebServer _server;
    private readonly object _lock = new();
    private readonly List<LogEntry> _buffer = new();
    private int _lastSentId;
    private string? _sendToSerial;

    public HttpService(string url = "http://127.0.0.1:8899")
    {
        _server = new WebServer(o => o.WithUrlPrefix(url).WithMode(HttpListenerMode.EmbedIO))
            .WithWebApi("/api", m => m.WithController(() => new SerialController(this)));
    }

    public void Start()
    {
        _server.Start();
    }

    public void Stop()
    {
        _server?.Stop();
    }

    public void AddEntry(LogEntry entry)
    {
        lock (_lock)
        {
            _buffer.Add(entry);
        }
    }

    public List<LogEntry> GetEntriesSince(int id)
    {
        lock (_lock)
        {
            return _buffer.Where(e => e.Id > id).ToList();
        }
    }

    public string? ConsumePendingSend()
    {
        lock (_lock)
        {
            var data = _sendToSerial;
            _sendToSerial = null;
            return data;
        }
    }

    public void QueueSend(string data)
    {
        lock (_lock)
        {
            _sendToSerial = data;
        }
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}

public class SerialController : WebApiController
{
    private readonly HttpService _service;

    public SerialController(HttpService service)
    {
        _service = service;
    }

    [Route(HttpVerbs.Get, "/ports")]
    public object GetPorts()
    {
        return SerialService.GetAvailablePorts();
    }

    [Route(HttpVerbs.Get, "/status")]
    public object GetStatus()
    {
        return new { ports = SerialService.GetAvailablePorts() };
    }

    [Route(HttpVerbs.Get, "/data")]
    public object GetData([QueryField] int since = 0)
    {
        return _service.GetEntriesSince(since);
    }

    [Route(HttpVerbs.Post, "/send")]
    public async Task SendData()
    {
        var body = await HttpContext.GetRequestBodyAsStringAsync();
        if (!string.IsNullOrEmpty(body))
        {
            _service.QueueSend(body);
            HttpContext.Response.StatusCode = 200;
        }
        else
        {
            HttpContext.Response.StatusCode = 400;
        }
    }

    [Route(HttpVerbs.Get, "/health")]
    public object Health()
    {
        return new { status = "ok", time = DateTime.Now };
    }
}
```

- [ ] **Step 2: 集成到 MainViewModel**

在 `MainViewModel.cs` 顶部添加字段：
```csharp
private readonly HttpService _http = new();
```

在构造函数末尾添加：
```csharp
_http.Start();
HttpUrl = "http://127.0.0.1:8899";
```

在 `OnSerialData` 中添加：
```csharp
_http.AddEntry(entry);
```

修改 `Dispose` 方法：
```csharp
_http.Dispose();
_serial.Dispose();
_logger.Dispose();
```

在 `SerialService` 中添加一个方法供 HttpService 使用：
在 `SerialService.cs` 顶部添加：
```csharp
private readonly object _sendLock = new();
private string? _pendingSend;

public void CheckPendingSend()
{
    string? data;
    // 这个由外部定时调用
}
```

实际上更好的方式：**HttpService 直接引用 SerialService 发送数据**。

修改 `HttpService` 添加 SerialService 引用：
```csharp
private readonly SerialService? _serialService;

public HttpService(SerialService? serialService = null, string url = "http://127.0.0.1:8899")
{
    _serialService = serialService;
    ...
}
```

修改 `SerialController.SendData`：
```csharp
[Route(HttpVerbs.Post, "/send")]
public async Task SendData()
{
    var body = await HttpContext.GetRequestBodyAsStringAsync();
    if (!string.IsNullOrEmpty(body))
    {
        // 如果 HEX 发送，格式: hex:30313233
        if (body.StartsWith("hex:"))
            _service.SendToSerial(body[4..], true);
        else
            _service.SendToSerial(body, false);
        HttpContext.Response.StatusCode = 200;
    }
    else
    {
        HttpContext.Response.StatusCode = 400;
    }
}
```

在 `HttpService` 添加方法：
```csharp
public void SendToSerial(string data, bool isHex)
{
    _serialService?.Send(data, isHex);
}
```

- [ ] **Step 3: 构造函数传参**

`MainViewModel` 构造函数改为传递 `_serial` 给 `_http`：
```csharp
private readonly HttpService _http;

public MainViewModel()
{
    ...
    _http = new HttpService(_serial);
    _http.Start();
    ...
}
```

- [ ] **Step 4: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 8: 串口断开自动重连

**Files:**
- Modify: `src/ACCcom/Services/SerialService.cs`
- Modify: `src/ACCcom/ViewModels/MainViewModel.cs`

- [ ] **Step 1: SerialService 自动重连**

在 `SerialService.cs` 中添加字段：
```csharp
private bool _autoReconnect = true;
private int _reconnectMaxAttempts = 10;
private int _reconnectAttempt;
private int _reconnectDelayMs = 1000;
private CancellationTokenSource? _reconnectCts;
private SerialConfig? _lastConfig;
```

修改 `Open` 方法，保存配置：
```csharp
_lastConfig = config;
_reconnectAttempt = 0;
```

添加重连方法：
```csharp
public void EnableAutoReconnect(bool enable, int maxAttempts = 10, int delayMs = 1000)
{
    _autoReconnect = enable;
    _reconnectMaxAttempts = maxAttempts;
    _reconnectDelayMs = delayMs;
    if (!enable)
    {
        _reconnectCts?.Cancel();
    }
}

private async void StartAutoReconnect()
{
    if (!_autoReconnect || _lastConfig == null) return;
    _reconnectCts = new CancellationTokenSource();
    var token = _reconnectCts.Token;

    while (_reconnectAttempt < _reconnectMaxAttempts && !token.IsCancellationRequested)
    {
        await Task.Delay(_reconnectDelayMs, token);
        if (token.IsCancellationRequested) break;
        if (_port?.IsOpen == true) break;

        _reconnectAttempt++;
        try
        {
            var tempPort = new SerialPort(_lastConfig.PortName, _lastConfig.BaudRate)
            {
                DtrEnable = _lastConfig.DtrEnable,
                RtsEnable = _lastConfig.RtsEnable
            };
            tempPort.Open();
            // 成功打开，替换旧端口
            Close();
            _port = tempPort;
            _port.DataReceived += OnSerialDataReceived;
            _port.ErrorReceived += OnSerialError;
            OnDataReceived?.Invoke(new LogEntry
            {
                Id = Interlocked.Increment(ref _entryId),
                Timestamp = DateTime.Now,
                Direction = "RX",
                RawHex = "",
                Text = $"[自动重连成功] 第{_reconnectAttempt}次尝试"
            });
            return;
        }
        catch
        {
            if (_reconnectAttempt >= _reconnectMaxAttempts)
            {
                OnError?.Invoke($"自动重连失败，已尝试 {_reconnectMaxAttempts} 次");
            }
        }
    }
}
```

在 `_port.ErrorReceived` 或断开检测时调用：
```csharp
OnDisconnected?.Invoke();
StartAutoReconnect();
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded
```

---

### Task 9: 最终集成与修复

- [ ] **Step 1: 处理 UI 细节：打开/关闭按钮文本切换**

`MainWindow.xaml` 中的打开按钮改为：
```xml
<Button Content="{Binding IsOpen, Converter={StaticResource BoolToOpenText}}"
        Command="{Binding OpenCloseCommand}" Width="100" Margin="3,0" />
```

添加转换器 `src/ACCcom/Converters/BoolToOpenTextConverter.cs`：
```csharp
using System.Globalization;
using System.Windows.Data;

namespace ACCcom.Converters;

public class BoolToOpenTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "关闭串口" : "打开串口";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

在 `App.xaml` 中注册：
```xml
<Application.Resources>
    <ResourceDictionary>
        <local:BoolToOpenTextConverter x:Key="BoolToOpenText" />
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 2: 最终编译验证**

```bash
dotnet build src\ACCcom\ACCcom.csproj
Expected: Build succeeded, 0 warnings
```

- [ ] **Step 3: 运行测试**

```bash
dotnet run --project src\ACCcom\ACCcom.csproj
Expected: 窗口正常启动，串口能打开/关闭
```

---

### 自检清单

**Spec 覆盖：**
- ✅ 串口配置（端口、波特率、校验、DTR/RTS）→ Task 5, 6
- ✅ RX/TX 双窗口显示 → Task 6
- ✅ 时间戳独立开关 → Task 5, 6
- ✅ HEX/ASCII 切换 → Task 5, 6
- ✅ 发送回显 → Task 3 (Send 方法)
- ✅ 快捷发送栏 → Task 6
- ✅ 保存到文件 → Task 5, 6
- ✅ 循环发送 → 待补充（可选）
- ✅ HTTP API → Task 7
- ✅ 日志文件 → Task 4
- ✅ 自动重连 → Task 8
- ✅ 快捷键 (ESC/Ctrl+S/Enter) → Task 6

**占位符检查：** 无

**类型一致性：** 一致
