namespace ACCcom.Core.Models;

public class AppSettings
{
    // Window position/size
    public double WindowX { get; set; } = double.NaN;
    public double WindowY { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = double.NaN;
    public double WindowHeight { get; set; } = double.NaN;

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

    // HTTP API security: when set, /api and /ws require the X-ACCcom-Token header
    // (or ?token= query parameter). Empty = token check disabled.
    public string HttpApiToken { get; set; } = "";
}
