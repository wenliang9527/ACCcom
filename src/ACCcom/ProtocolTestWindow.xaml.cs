using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class ProtocolTestWindow : Window
{
    private readonly ProtocolTestViewModel _vm;

    public ProtocolTestWindow(ProtocolTestViewModel vm)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        _vm = vm;
        DataContext = vm;
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
    }
}