using System.Windows;
using ACCcom.Core.Services;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class ModbusConnectionDialog : Window
{
    public ModbusService? Result { get; private set; }

    public ModbusConnectionDialog(ModbusConnectionManager manager, ModbusService defaultService)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        var vm = new ModbusConnectionViewModel(manager, svc =>
        {
            Result = svc ?? defaultService;
            Dispatcher.BeginInvoke(() => DialogResult = true);
        });
        DataContext = vm;
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
