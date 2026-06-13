namespace ACCcom.ViewModels;

public class PortItemViewModel
{
    public string Tag { get; set; } = "";
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; }
    public bool IsOpen { get; set; }
    public int RxCount { get; set; }
}
