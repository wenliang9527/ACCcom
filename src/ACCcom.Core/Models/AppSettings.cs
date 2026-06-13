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
}
