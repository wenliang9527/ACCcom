using System.Windows;
using System.Windows.Input;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _vm.RxEntries.Clear();
            _vm.TxEntries.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_vm.SaveRxCommand.CanExecute(null))
                _vm.SaveRxCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _vm.SendText += "\r\n";
            }
            else
            {
                _vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
