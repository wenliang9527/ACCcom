using System.Windows;
using ACCcom.Core.Services;

namespace ACCcom;

public partial class CreateSlaveDialog : Window
{
    private readonly ModbusSlaveService _slaveService;

    public CreateSlaveDialog(ModbusSlaveService slaveService)
    {
        InitializeComponent();
        _slaveService = slaveService;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (!byte.TryParse(SlaveIdBox.Text, out var slaveId))
        {
            MessageBox.Show("Invalid Slave ID", "Error");
            return;
        }
        var transport = TransportBox.SelectedIndex == 0 ? "tcp" : "rtu";
        var port = PortBox.Text;
        if (!int.TryParse(RegsBox.Text, out var regs) || regs < 1)
        {
            MessageBox.Show("Invalid holding register count", "Error");
            return;
        }
        _slaveService.CreateSlave(slaveId, transport, port, holdingRegisters: regs);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}