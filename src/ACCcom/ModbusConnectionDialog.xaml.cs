using System.Windows;
using ACCcom.Core.Services;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class ModbusConnectionDialog : Window
{
    public ModbusService? Result { get; private set; }

    public ModbusConnectionDialog(ModbusConnectionManager manager, ModbusService defaultService)
    {
        InitializeComponent();
        var vm = new ModbusConnectionViewModel(manager, svc =>
        {
            Result = svc ?? defaultService;
            Dispatcher.BeginInvoke(() => DialogResult = true);
            return true;
        });
        DataContext = vm;
    }
}
