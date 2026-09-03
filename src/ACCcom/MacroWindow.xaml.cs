using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class MacroWindow : Window
{
    public MacroWindow(MacroViewModel vm)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        DataContext = vm;
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}