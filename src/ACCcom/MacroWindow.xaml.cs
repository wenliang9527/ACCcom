using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MacroWindow : Window
{
    public MacroWindow(MacroViewModel vm)
    {
        InitializeComponent();
        WindowHelper.AttachWindowState(this, "MacroWindow");
        DataContext = vm;
    }

}
