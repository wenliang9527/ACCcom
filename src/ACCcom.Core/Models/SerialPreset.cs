namespace ACCcom.Core.Models;

public class SerialPreset
{
    public string Name { get; set; } = "";
    public string Port { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public int StopBits { get; set; } = 1;
    public int Parity { get; set; } = 0;
    public bool Dtr { get; set; }
    public bool Rts { get; set; }
}
