using System.Windows;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class ProtocolTestWindow : Window
{
    private readonly ProtocolTestViewModel _vm;

    public ProtocolTestWindow(ProtocolTestViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
    }
}
