# ACCCOM 协议解析引擎 — 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**目标：** 为 ACCCOM 增加用户自定义 C# 脚本解析器，串口数据到达后自动经过脚本解析，带色标注字段含义，并在 UI 中展示字段树。

**架构：** 用户将 `.csx` 脚本放在 `parsers/` 目录，ParserEngine 用 Roslyn 编译并执行脚本，ParserManager 管理脚本列表和激活切换。解析结果 `FieldAnnotation` 列表挂载到 LogEntry 上，UI 渲染字段树和颜色标记。

**Tech Stack:** .NET 8.0 WPF, Microsoft.CodeAnalysis.CSharp.Scripting, MVVM

---

### 涉及文件清单

| 操作 | 文件 |
|------|------|
| **Create** | `Models/FieldAnnotation.cs` |
| **Create** | `Services/ScriptGlobals.cs` |
| **Create** | `Services/ParserEngine.cs` |
| **Create** | `Services/ParserManager.cs` |
| **Create** | `Converters/SeverityToColorConverter.cs` |
| **Create** | `parsers/sample.csx` |
| **Modify** | `ACCcom.csproj` — 添加 Roslyn 包 |
| **Modify** | `Models/LogEntry.cs` — 添加 Fields 属性 |
| **Modify** | `ViewModels/MainViewModel.cs` — 集成解析流程 |
| **Modify** | `App.xaml` — 注册新 Converter |
| **Modify** | `MainWindow.xaml` — 添加解析器选择器和字段面板 |

---

### Task 1: FieldAnnotation 模型 + ScriptGlobals + Roslyn 依赖

**Files:**
- Create: `Models/FieldAnnotation.cs`
- Create: `Services/ScriptGlobals.cs`
- Modify: `ACCcom.csproj`
- Modify: `Models/LogEntry.cs`

- [ ] **Step 1: 创建 FieldAnnotation 模型**

`Models/FieldAnnotation.cs`:

```csharp
namespace ACCcom.Models;

public enum FieldSeverity
{
    Normal,
    Warning,
    Error
}

public class FieldAnnotation
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
    public int Length { get; set; }
    public string RawHex { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string? Color { get; set; }
    public FieldSeverity Severity { get; set; }
}
```

- [ ] **Step 2: 创建 ScriptGlobals（脚本中的全局上下文）**

`Services/ScriptGlobals.cs`:

```csharp
using ACCcom.Models;

namespace ACCcom.Services;

public class ScriptGlobals
{
    public byte[] RawData { get; set; } = [];
    public DateTime Timestamp { get; set; }

    public string RawHex(int offset, int length)
    {
        var span = RawData.AsSpan(offset, Math.Min(length, RawData.Length - offset));
        return BitConverter.ToString(span.ToArray()).Replace("-", " ");
    }

    public ushort ToUInt16(int offset, bool bigEndian = false)
    {
        if (offset + 1 >= RawData.Length) return 0;
        return bigEndian
            ? (ushort)((RawData[offset] << 8) | RawData[offset + 1])
            : (ushort)((RawData[offset + 1] << 8) | RawData[offset]);
    }

    public ushort Crc16(int offset, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
        {
            crc ^= RawData[i];
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    public byte Sum8(int offset, int length)
    {
        byte sum = 0;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
            sum += RawData[i];
        return sum;
    }

    public byte Xor8(int offset, int length)
    {
        byte xor = 0;
        for (int i = offset; i < offset + length && i < RawData.Length; i++)
            xor ^= RawData[i];
        return xor;
    }

    public int ToInt16(int offset, bool bigEndian = false) => (short)ToUInt16(offset, bigEndian);

    public float ToFloat(int offset, bool bigEndian = false)
    {
        if (offset + 3 >= RawData.Length) return 0;
        var bytes = RawData[offset..(offset + 4)];
        if (bigEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes);
    }
}
```

- [ ] **Step 3: 添加 Roslyn NuGet 包**

`ACCcom.csproj` 中添加：

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="4.11.0" />
```

运行：`dotnet restore`

- [ ] **Step 4: LogEntry 添加 Fields 属性**

`Models/LogEntry.cs`：

```csharp
namespace ACCcom.Models;

public class LogEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = "";
    public string RawHex { get; set; } = "";
    public string Text { get; set; } = "";
    public List<FieldAnnotation>? Fields { get; set; }
}
```

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom/Models/FieldAnnotation.cs src/ACCcom/Services/ScriptGlobals.cs src/ACCcom/ACCcom.csproj src/ACCcom/Models/LogEntry.cs
git commit -m "feat: add FieldAnnotation model, ScriptGlobals, Roslyn dep"
```

---

### Task 2: ParserEngine — 编译执行 .csx 脚本

**Files:**
- Create: `Services/ParserEngine.cs`

- [ ] **Step 1: 创建 ParserEngine**

`Services/ParserEngine.cs`：

```csharp
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ACCcom.Models;

namespace ACCcom.Services;

public class ParserEngine
{
    private Script<List<FieldAnnotation>>? _compiled;
    private string? _currentCode;
    private string? _lastError;

    public string? LastError => _lastError;

    public bool Load(string code)
    {
        try
        {
            var options = ScriptOptions.Default
                .WithImports("System", "System.Collections.Generic", "System.Linq", "ACCcom.Models")
                .WithReferences(typeof(FieldAnnotation).Assembly);

            _compiled = CSharpScript.Create<List<FieldAnnotation>>(code, options, globalsType: typeof(ScriptGlobals));
            _compiled.Compile();
            _currentCode = code;
            _lastError = null;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _compiled = null;
            return false;
        }
    }

    public List<FieldAnnotation>? Execute(byte[] data, DateTime timestamp)
    {
        if (_compiled == null) return null;

        try
        {
            var globals = new ScriptGlobals { RawData = data, Timestamp = timestamp };
            var result = _compiled.RunAsync(globals).GetAwaiter().GetResult();
            return result.ReturnValue;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return null;
        }
    }

    public void Clear()
    {
        _compiled = null;
        _currentCode = null;
        _lastError = null;
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add src/ACCcom/Services/ParserEngine.cs
git commit -m "feat: add ParserEngine with Roslyn script compilation"
```

---

### Task 3: ParserManager — 脚本文件扫描和激活管理

**Files:**
- Create: `Services/ParserManager.cs`
- Create: `parsers/sample.csx`

- [ ] **Step 1: 创建 ParserManager**

`Services/ParserManager.cs`：

```csharp
using System.Collections.ObjectModel;
using System.IO;
using ACCcom.Models;

namespace ACCcom.Services;

public class ParserManager : IDisposable
{
    private readonly string _parserDir;
    private readonly ParserEngine _engine = new();
    private FileSystemWatcher? _watcher;
    private string? _activeParserPath;

    public ObservableCollection<string> AvailableParsers { get; } = new();
    public string? ActiveParserName { get; private set; }
    public string? LastError => _engine.LastError;
    public ParserEngine Engine => _engine;

    public ParserManager()
    {
        _parserDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        Directory.CreateDirectory(_parserDir);
        SetupWatcher();
        Refresh();
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(_parserDir, "*.csx")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };
        _watcher.Created += (_, _) => Refresh();
        _watcher.Deleted += (_, _) => Refresh();
        _watcher.Changed += (_, _) =>
        {
            if (_activeParserPath != null && File.Exists(_activeParserPath))
            {
                var code = File.ReadAllText(_activeParserPath);
                _engine.Load(code);
            }
        };
    }

    public void Refresh()
    {
        var files = Directory.GetFiles(_parserDir, "*.csx")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x);

        AvailableParsers.Clear();
        AvailableParsers.Add("(无)");
        foreach (var f in files)
            AvailableParsers.Add(f!);

        if (ActiveParserName != null && !AvailableParsers.Contains(ActiveParserName))
            Activate(null);
    }

    public bool Activate(string? parserName)
    {
        if (string.IsNullOrEmpty(parserName) || parserName == "(无)")
        {
            ActiveParserName = null;
            _activeParserPath = null;
            _engine.Clear();
            return true;
        }

        var path = Path.Combine(_parserDir, parserName + ".csx");
        if (!File.Exists(path)) return false;

        var code = File.ReadAllText(path);
        if (!_engine.Load(code)) return false;

        ActiveParserName = parserName;
        _activeParserPath = path;
        return true;
    }

    public string GetParserDir() => _parserDir;

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
```

- [ ] **Step 2: 创建示例脚本**

`parsers/sample.csx`：

```csharp
// sample.csx — 示例解析脚本
// 协议：自定义温控仪
// 帧头: AA 55, 第3字节=长度, 末字节=CRC16低8位

var result = new List<FieldAnnotation>();

if (RawData.Length < 5) return result;

result.Add(new FieldAnnotation
{
    Name = "帧头",
    Offset = 0,
    Length = 2,
    RawHex = RawHex(0, 2),
    DisplayValue = "AA 55",
    Color = "#888888"
});

result.Add(new FieldAnnotation
{
    Name = "长度",
    Offset = 2,
    Length = 1,
    RawHex = RawHex(2, 1),
    DisplayValue = $"{RawData[2]} 字节"
});

result.Add(new FieldAnnotation
{
    Name = "温度",
    Offset = 4,
    Length = 1,
    RawHex = RawHex(4, 1),
    DisplayValue = $"{RawData[4]} °C",
    Severity = RawData[4] > 80 ? FieldSeverity.Warning : FieldSeverity.Normal
});

return result;
```

- [ ] **Step 3: 设置 .csx 复制到输出目录**

`ACCcom.csproj` 中添加：

```xml
<ItemGroup>
    <None Update="parsers\**\*">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
</ItemGroup>
```

- [ ] **Step 4: 提交**

```bash
git add src/ACCcom/Services/ParserManager.cs src/ACCcom/parsers/sample.csx
git commit -m "feat: add ParserManager with file watcher and sample script"
```

---

### Task 4: SeverityToColorConverter

**Files:**
- Create: `Converters/SeverityToColorConverter.cs`
- Modify: `App.xaml`

- [ ] **Step 1: 创建 Converter**

`Converters/SeverityToColorConverter.cs`：

```csharp
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ACCcom.Models;

namespace ACCcom.Converters;

public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FieldSeverity severity)
        {
            return severity switch
            {
                FieldSeverity.Warning => new SolidColorBrush(Color.FromRgb(250, 204, 21)),
                FieldSeverity.Error => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: 注册到 App.xaml**

`App.xaml` 中添加（在已有 converters 声明旁边）：

```xml
<converters:SeverityToColorConverter x:Key="SeverityToColor" />
```

- [ ] **Step 3: 提交**

```bash
git add src/ACCcom/Converters/SeverityToColorConverter.cs
git commit -m "feat: add SeverityToColorConverter"
```

---

### Task 5: MainViewModel 集成解析流程

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

改动点：
1. 添加 ParserManager 实例和解析器相关属性
2. 在 OnSerialData 中插入解析步骤
3. 添加选择 LogEntry 时的字段列表绑定

- [ ] **Step 1: 添加 ParserManager 和字段相关属性**

在 `MainViewModel` 现有字段声明区添加：

```csharp
private readonly ParserManager _parserManager = new();

private string _selectedParser = "(无)";
public string SelectedParser
{
    get => _selectedParser;
    set
    {
        if (SetField(ref _selectedParser, value))
            _parserManager.Activate(value);
    }
}

public ObservableCollection<string> AvailableParsers => _parserManager.AvailableParsers;
public string? ParserError => _parserManager.LastError;

private LogEntry? _selectedEntry;
public LogEntry? SelectedEntry
{
    get => _selectedEntry;
    set => SetField(ref _selectedEntry, value);
}
```

- [ ] **Step 2: 修改 OnSerialData 加入解析逻辑**

将原 `OnSerialData` 方法中 `RxEntries.Add(entry);` 之前插入解析：

```csharp
private void OnSerialData(LogEntry entry)
{
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
        _logger.Write(entry);
        _http.AddEntry(entry);

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
```

- [ ] **Step 3: 添加打开脚本目录命令**

在命令声明区添加：

```csharp
public ICommand OpenParserDirCommand { get; }

// 构造函数中添加：
OpenParserDirCommand = new RelayCommand(_ => OpenParserDir());

// 方法：
private void OpenParserDir()
{
    var dir = _parserManager.GetParserDir();
    if (Directory.Exists(dir))
        System.Diagnostics.Process.Start("explorer.exe", dir);
}
```

- [ ] **Step 4: 修改 Dispose**

在 `Dispose` 中添加 `_parserManager.Dispose();`

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom/ViewModels/MainViewModel.cs
git commit -m "feat: integrate ParserManager into MainViewModel data flow"
```

---

### Task 6: UI — 解析器选择和字段详情面板

**Files:**
- Modify: `MainWindow.xaml`

- [ ] **Step 1: 工具栏添加解析器选择**

在 Row 0 的 DTR/RTS 区域后添加（Grid.Column="2" 的 WrapPanel 内，已有控件的右侧）：

```xml
<Rectangle Style="{StaticResource ConfigSeparator}" Margin="8,0,8,0" />
<TextBlock Text="解析器" Style="{StaticResource ConfigLabel}" />
<ComboBox ItemsSource="{Binding AvailableParsers}"
          Text="{Binding SelectedParser, UpdateSourceTrigger=PropertyChanged}"
          Width="110" IsEditable="False" Margin="0,0,4,0" />
<Button Content="📂" Command="{Binding OpenParserDirCommand}"
        Style="{StaticResource OutlineButton}" FontSize="14" Padding="6,2"
        ToolTip="打开脚本目录" />
```

- [ ] **Step 2: RX 面板底部添加字段详情**

在 RX 面板 (`Grid.Column="0"` 的 Border 内)，原 DockPanel 的底部添加：

```xml
<!-- Field detail panel -->
<Border DockPanel.Dock="Bottom"
        Background="{StaticResource BgBaseBrush}"
        BorderThickness="0,1,0,0"
        BorderBrush="{StaticResource BorderBrush}"
        Padding="8,6"
        Height="160"
        Visibility="{Binding SelectedEntry.Fields, Converter={StaticResource NullToVisibility}}">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding SelectedEntry.Fields}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <Rectangle Grid.Column="0" Width="4" Margin="0,0,6,0"
                                   Fill="{Binding Severity, Converter={StaticResource SeverityToColor}}"
                                   RadiusX="2" RadiusY="2" />
                        <TextBlock Grid.Column="1" Text="{Binding Name}"
                                   FontWeight="SemiBold" FontSize="12"
                                   Foreground="{StaticResource InkPrimaryBrush}"
                                   MinWidth="80" />
                        <WrapPanel Grid.Column="2">
                            <TextBlock Text="{Binding RawHex}" FontFamily="Consolas" FontSize="12"
                                       Foreground="{StaticResource AccentBrush}" Margin="0,0,8,0" />
                            <TextBlock Text="→" FontSize="12"
                                       Foreground="{StaticResource InkTertiaryBrush}" Margin="0,0,8,0" />
                            <TextBlock Text="{Binding DisplayValue}" FontSize="12"
                                       Foreground="{StaticResource InkPrimaryBrush}" />
                        </WrapPanel>
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Border>
```

- [ ] **Step 3: 添加 NullToVisibilityConverter 并注册**

`Converters/NullToVisibilityConverter.cs`：

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ACCcom.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

`App.xaml` 注册：

```xml
<converters:NullToVisibilityConverter x:Key="NullToVisibility" />
```

- [ ] **Step 4: RX ListBox 增加选中绑定**

将 RX 的 ListBox 添加 `SelectedItem` 绑定：

```xml
<ListBox ItemsSource="{Binding RxEntries}"
         SelectedItem="{Binding SelectedEntry}"
         ...>
```

- [ ] **Step 5: 提交**

```bash
git add src/ACCcom/MainWindow.xaml src/ACCcom/Converters/NullToVisibilityConverter.cs
git commit -m "feat: add parser selector and field detail panel to UI"
```

---

### 自检

- [ ] 所有模型、服务、视图模型文件清单已覆盖
- [ ] 每个步骤有完整代码，无 TODO/TBD
- [ ] 类型一致性校验：FieldAnnotation 属性名、ScriptGlobals 方法签名、ViewModel 绑定名在 Task 之间一致
- [ ] csproj 修改两处已覆盖（Roslyn 包 + parsers 复制）
- [ ] App.xaml 修改两处已覆盖（SeverityToColor + NullToVisibility Converter）
