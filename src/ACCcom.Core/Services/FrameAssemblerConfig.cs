namespace ACCcom.Core.Services;

public class FrameAssemblerConfig
{
    public string Header { get; set; } = "";
    public int LengthFieldOffset { get; set; } = -1;
    public int LengthFieldSize { get; set; } = 1;
    public int MaxFrameSize { get; set; } = 4096;
    public int PartialFrameTimeoutMs { get; set; } = 2000;
    public bool Enabled { get; set; }
}
