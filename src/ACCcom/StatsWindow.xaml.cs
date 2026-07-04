using System.Windows;
using ACCcom.Helpers;

namespace ACCcom;

public partial class StatsWindow : Window
{
    public StatsWindow()
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
