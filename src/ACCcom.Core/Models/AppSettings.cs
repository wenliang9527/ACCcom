namespace ACCcom.Core.Models;

public class AppSettings
{
    // Window position/size
    public double WindowX { get; set; } = double.NaN;
    public double WindowY { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = double.NaN;
    // Whether the main window was maximized at exit (normal bounds above are
    // still saved, so restoring goes back to Normal cleanly).
    public bool WindowMaximized { get; set; }

    // Theme
    public bool IsDarkTheme { get; set; }
    // Active theme id (see ThemeManager). Empty = derive from IsDarkTheme (legacy settings).
    public string Theme { get; set; } = "";

    // Language
    public string Language { get; set; } = "zh-CN";

    // Serial port config
    public string LastPort { get; set; } = "";
    public int LastBaudRate { get; set; } = 115200;
    public int LastDataBits { get; set; } = 8;
    // StopBits/Parity store the ComboBox SelectedIndex (0/1/2), matching
    // ConnectionViewModel.SelectedStopBits / SelectedParity.
    public int LastStopBits { get; set; } = 1;
    public int LastParity { get; set; }

    // Connection panel: last connection type ("Serial"/"TCP"/"UDP"), network
    // endpoint, control lines and auto-reconnect toggle — restored on launch so
    // reconnect workflows don't retype them every session.
    public string LastConnectionType { get; set; } = "Serial";
    public string LastNetworkHost { get; set; } = "127.0.0.1";
    public int LastNetworkPort { get; set; } = 4001;
    public bool LastDtrEnable { get; set; }
    public bool LastRtsEnable { get; set; }
    public bool LastAutoReconnect { get; set; } = true;

    // Hex display modes
    public bool IsHexSend { get; set; }
    public bool IsHexDisplayRx { get; set; }
    public bool IsHexDisplayTx { get; set; }

    // Timestamp toggles
    public bool EnableRxTimestamp { get; set; } = true;
    public bool EnableTxTimestamp { get; set; } = true;

    // Parser engine
    public int ParserCacheSize { get; set; } = 10;

    // Buffer
    public int BufferCapacity { get; set; } = 10000;

    // Display
    public int MaxDisplayEntries { get; set; } = 10000;

    // Quick send sidebar
    public bool ShowQuickSendSidebar { get; set; } = true;
    public double QuickSendSidebarWidth { get; set; } = 260;

    // Data panel: false = single combined RX+TX list (default); true = classic
    // side-by-side split. DataPaneSplitRatio is the RX column's share (0..1)
    // when split; values outside (0.05, 0.95) fall back to equal columns.
    public bool SplitDataPanes { get; set; }
    public double DataPaneSplitRatio { get; set; } = 0.5;

    // HTTP API security: when set, /api and /ws require the X-ACCcom-Token header
    // (or ?token= query parameter). Empty = token check disabled.
    public string HttpApiToken { get; set; } = "";

    // Send history: recent texts entered in the send box, newest last.
    // Capped at MaxSendHistory entries (oldest dropped).
    public List<string> SendHistory { get; set; } = new();
    public int MaxSendHistory { get; set; } = 50;

    // Per-column widths (pixels) for the parsed-field DataGrid in DataPanel, keyed by
    // the column's zero-based index in the XAML column list. DataGridTextColumn has
    // no Tag property, so we use the index instead. Missing / out-of-range = default.
    public Dictionary<int, double> FieldGridColumnWidths { get; set; } = new();

    // Per-column widths (pixels) for the MCP traffic window's ListView, keyed by
    // the column's zero-based index. Same index-keyed scheme as FieldGridColumnWidths
    // — GridViewColumn has no Tag property.
    public Dictionary<int, double> McpTrafficColumnWidths { get; set; } = new();

    // Restored position/size of secondary windows (StatsWindow, MacroWindow, …),
    // keyed by window class name. Missing key = window opens at its XAML default.
    public Dictionary<string, WindowRect> WindowStates { get; set; } = new();
}

/// <summary>Saved window placement in device-independent pixels. Width/Height are
/// null for fixed-size (NoResize) windows — only the position is persisted.</summary>
public record WindowRect(double X, double Y, double? Width = null, double? Height = null);
