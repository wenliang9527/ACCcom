# ACCcom 界面视觉升级方案

> 分析范围：`MainWindow.xaml`、`App.xaml`、`DarkTheme.xaml`、`LightTheme.xaml`、`ModbusWindow.xaml`、`PlotWindow.xaml`、`StatsWindow.xaml`
>
> 策略：在现有 Tech Blue 配色基础上精调，不改变整体布局结构

---

## 一、现状分析

### 1.1 整体评价

ACCcom 的 UI 基础质量已经不错 — 有完整的深色/浅色双主题、语义化的设计 Token（`BgBase`/`BgSurface`/`Accent` 等）、自定义组件模板（Button/ComboBox/CheckBox/ScrollBar）。整体走的是 "Tech Minimal" 路线，与串口调试工具的专业定位契合。

### 1.2 存在的问题

| 问题 | 位置 | 严重程度 |
|------|------|----------|
| **状态栏信息重复且拥挤** | `MainWindow.xaml` Row 5 | 高 — 右侧有三个 `StatusBarItem`，其中两个都显示 RX/TX 计数和字节数，信息冗余 |
| **工具栏控件密度过高** | `MainWindow.xaml` Row 0 | 中 — 大量按钮/下拉框/复选框挤在一行，使用 `WrapPanel` 在窄屏下换行体验差 |
| **Emoji 混用** | 多处 | 中 — 搜索图标用 `🔍`、文件夹用 `📂`、链接用 `🔗`、设置用 `⚙`，在不同系统/字体下渲染不一致，视觉上不够精致 |
| **搜索高亮色刺眼** | `MainWindow.xaml` #40FFFF00 | 中 — 黄色高亮在深色主题下过于刺眼 |
| **深色主题对比度不足** | `DarkTheme.xaml` | 中 — `InkTertiary #6B6B76` 在 `#121214` 背景上对比度仅 4.2:1，略低于 WCAG AA 标准的 4.5:1 |
| **DataGrid/ListView 缺少统一风格** | `ModbusWindow.xaml` | 低 — Modbus 窗口的 `ListView` 使用默认样式，未与主窗口的 DataGrid 风格对齐 |
| **窗口标题栏未自定义** | 所有窗口 | 低 — 仍使用系统默认标题栏，与整体深色风格不够融合（可选优化） |
| **多处用 TextBox 模拟只读文本** | `StatsWindow.xaml`、状态栏 | 低 — 应该用 `TextBlock`，减少不必要的模板开销 |

---

## 二、配色精调方案

### 2.1 深色主题 (DarkTheme.xaml)

**目标：提升对比度，增加层次感**

| Token | 现有值 | 建议值 | 原因 |
|-------|--------|--------|------|
| `InkTertiary` | `#6B6B76` | `#7A7A88` | 将对比度提升至 ~5.1:1，满足 WCAG AA |
| `BgSurface` | `#1A1A1C` | `#18181B` | 与 `BgBase` 差距从 4 级灰加深到 5 级，层次更清晰 |
| `BgElevated` | `#222228` | `#202026` | 微调，与 `BgSurface` 区分更明显 |
| `BgHover` | `#2A2A30` | `#27272F` | 略微降低亮度，hover 反馈更克制 |
| `BgActive` | `#333340` | `#30303C` | 同上 |
| `Accent` | `#5E9BFF` | `#60A5FA` | 色相从偏紫蓝调整为更纯正的蓝，与 Tailwind blue-400 对齐，视觉更现代 |
| `AccentHover` | `#7EB3FF` | `#93C5FD` | 对应 blue-300 |
| `AccentPressed` | `#4A85E0` | `#3B82F6` | 对应 blue-500，按下态更有分量 |
| `StatusGreen` | `#34D673` | `#4ADE80` | 色相调整，更接近标准绿色，辨识度更高 |
| `RedError` | `#F87171` | `#F87171` | 保持不变 |
| `BorderColor` | `#2A2A30` | `#27272A` | 更微妙的边框，减少视觉噪音 |
| `BorderLight` | `#3A3A44` | `#3F3F46` | 对应 zinc-700 |
| `DividerBrush` | `#2A2A30` | `#27272A` | 与 BorderColor 统一 |

### 2.2 浅色主题 (LightTheme.xaml)

**目标：保持清爽感，微调色温一致性**

| Token | 现有值 | 建议值 | 原因 |
|-------|--------|--------|------|
| `BgSurface` | `#F8F9FB` | `#F9FAFB` | 对应 gray-50，更标准 |
| `BgHover` | `#F0F3F8` | `#F3F4F6` | 对应 gray-100，色温一致性更好 |
| `BgActive` | `#E6EBF2` | `#E5E7EB` | 对应 gray-200 |
| `BorderColor` | `#E2E5EA` | `#E5E7EB` | 对应 gray-200，统一色阶 |
| `BorderLight` | `#CDD2DA` | `#D1D5DB` | 对应 gray-300 |
| `Accent` | `#3478F6` | `#3B82F6` | 与深色主题的 Accent 统一为 blue-500 |
| `AccentHover` | `#5A92FF` | `#60A5FA` | 对应 blue-400 |
| `AccentPressed` | `#2563DB` | `#2563EB` | 对应 blue-600 |
| `InkTertiary` | `#8B92A0` | `#9CA3AF` | 对应 gray-400，色温更中性 |
| `StatusGreen` | `#22C55E` | `#22C55E` | 保持不变 |
| `DividerBrush` | `#EAECF0` | `#E5E7EB` | 与 BorderColor 统一 |

---

## 三、组件优化方案

### 3.1 主窗口 (MainWindow.xaml)

#### 3.1.1 状态栏精简（优先级：高）

**现状**：右侧有三个 `StatusBarItem`，信息大量重复：
- 第二个：显示 "RX 123" / "TX 456" 计数徽章
- 第三个：显示 "RX: 1234 bytes | TX: 567 bytes | Errors: 0 | Up: 00:05:23"

**建议**：合并为一个信息条，去掉重复的计数徽章，保留完整的统计信息：

```
左侧: ● 连接状态  |  状态文本
右侧: RX: 1234 bytes | TX: 567 bytes | Errors: 0 | Up: 00:05:23 | http://...
```

减少约 40px 的水平空间占用。

#### 3.1.2 工具栏分区优化（优先级：中）

**现状**：所有控件平铺在 `WrapPanel` 中，依赖分隔符 `Rectangle` 分组。

**建议**：将工具栏分为三个视觉区域，用 `Margin` 间距代替 `Rectangle` 分隔符：

| 区域 | 内容 | 建议 |
|------|------|------|
| 连接区 | 模式/端口/波特率/网络配置/打开关闭按钮 | 保持，用 `Margin="0,0,16,0"` 分隔 |
| 工具区 | 解析器/Diff/Plot/Stats/Modbus | 用 `Margin="0,0,16,0"` 分隔 |
| 显示区 | HEX/正则/时间戳/自动滚动/主题切换 | 移至右侧与连接状态徽章并排 |

#### 3.1.3 Emoji 替换为 Text 图标（优先级：中）

将所有 Emoji 替换为 Unicode 几何符号或简洁的 ASCII 字符：

| 现有 | 替换为 | 位置 |
|------|--------|------|
| `🔍` | 搜索图标保留，但用 `FontFamily="Segoe Fluent Icons"` 或 `Marlett` | RX/TX 搜索 |
| `📂` | `⌐` 或路径符号 | 打开解析器目录 |
| `🔗` | `⊞` 或省略 | Frame Assembler |
| `⚙` | `Ξ` 或用 Path 画齿轮 | 高级参数 |
| `⇅` | `↕` | 自动滚动 ToggleButton |

> **备选方案**：如果不想用特殊字体，可以考虑用 `Path Data` 画简单的几何图标（圆形、三角、箭头），这样跨平台渲染最稳定。但这会显著增加 XAML 代码量，建议在第一阶段先替换为稳定的 Unicode 符号。

#### 3.1.4 搜索高亮色优化（优先级：中）

**现状**：搜索匹配高亮为 `#40FFFF00`（不透明度 25% 的黄色），在深色主题下偏刺眼。

**建议**：
- 深色主题：`#30FFE066`（更偏橙黄，降低不透明度到 ~19%）
- 浅色主题：`#50FFF3B0`（柔和黄色高亮）

改为使用 DynamicResource 引用主题色，在 Theme 文件中新增 Token：

```xml
<!-- DarkTheme.xaml -->
<Color x:Key="SearchHighlight">#30FFE066</Color>

<!-- LightTheme.xaml -->
<Color x:Key="SearchHighlight">#50FFF3B0</Color>
```

### 3.2 Modbus 窗口 (ModbusWindow.xaml)

#### 3.2.1 TabControl 样式优化

**现状**：使用 WPF 默认 `TabControl`，与整体深色风格不协调。

**建议**：在 `App.xaml` 中添加自定义 `TabControl` 样式：

- Tab 头部：无边框、背景透明、文字用 `InkSecondaryBrush`
- 选中 Tab：底部 2px Accent 色条、文字用 `InkPrimaryBrush` + `SemiBold`
- 内容区：无边框、直接填充内容

#### 3.2.2 ListView 样式统一

**现状**：使用 `ListView + GridView`，依赖默认样式。

**建议**：应用与主窗口 `FieldDataGrid` 相似的样式：
- 行高 26px
- 字体 Consolas 12px
- 表头用 `BgSurfaceBrush` 背景 + `InkSecondaryBrush` 文字
- 行 hover 用 `BgHoverBrush`
- 行选中用 `BgActiveBrush`

### 3.3 Stats 窗口 (StatsWindow.xaml)

#### 3.3.1 只读文本改用 TextBlock

**现状**：大量 `TextBox IsReadOnly="True" IsTabStop="False" BorderThickness="0" Background="Transparent"` 来显示只读数据。

**建议**：全部替换为 `TextBlock`，减少 DOM 层级和模板开销。

### 3.4 Plot 窗口 (PlotWindow.xaml)

#### 3.4.1 同样替换只读 TextBox

同 Stats 窗口处理。

---

## 四、间距与圆角规范化

### 4.1 间距系统

当前间距值不统一，建议建立 4px 基准间距系统：

| 用途 | 建议值 | 现有范围 |
|------|--------|----------|
| 紧凑（控件内间距） | 4px | 3-5px |
| 小（相邻控件） | 8px | 4-12px |
| 中（区块内分隔） | 12px | 8-16px |
| 大（区块间分隔） | 16px | 10-20px |
| 特大（面板外边距） | 20px | 8-16px |

### 4.2 圆角统一

| 元素 | 现有圆角 | 建议圆角 |
|------|----------|----------|
| 卡片/面板 | 8px | 8px（保持） |
| 按钮 | 5px | 6px（微调，更圆润） |
| 输入框 | 5px | 6px（统一） |
| 标签/徽章 | 13px / 11px | 12px / 10px |
| 下拉弹窗 | 6px | 8px（与卡片统一） |
| 分隔符（视觉） | 2px | 2px（保持） |

---

## 五、实施优先级

| 优先级 | 改动项 | 涉及文件 | 预计工作量 |
|--------|--------|----------|------------|
| P0 | 配色 Token 调整 | `DarkTheme.xaml`、`LightTheme.xaml` | 小 |
| P0 | 搜索高亮色优化 | `MainWindow.xaml`、Theme 文件 | 小 |
| P1 | 状态栏精简合并 | `MainWindow.xaml` | 小 |
| P1 | Emoji 替换 | `MainWindow.xaml`、各子窗口 | 小 |
| P1 | 只读 TextBox → TextBlock | `StatsWindow.xaml`、`PlotWindow.xaml`、状态栏 | 小 |
| P2 | 工具栏分区优化 | `MainWindow.xaml` | 中 |
| P2 | Modbus TabControl/ListView 样式 | `App.xaml`、`ModbusWindow.xaml` | 中 |
| P2 | 间距/圆角微调 | 全部 XAML 文件 | 中 |
| P3 | 窗口标题栏自定义（可选） | 所有窗口 `.xaml` + `.xaml.cs` | 大 |

---

## 六、备注

- 以上改动**不涉及任何 C# 代码逻辑变更**（除标题栏自定义需要 code-behind 支持）
- 所有配色建议值参考 Tailwind CSS 色阶体系（gray/zinc/blue），确保色温一致性
- 建议按优先级分批执行，每批完成后构建验证效果
