using System.Windows;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class VirtualSerialWindow : Window
{
    private readonly VirtualSerialViewModel _vm;

    public VirtualSerialWindow(VirtualSerialViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

}
