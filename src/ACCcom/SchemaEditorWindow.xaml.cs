using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class SchemaEditorWindow : Window
{
    public SchemaEditorWindow(SchemaEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WindowHelper.SetupTitleBar(this, TitleBar);
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
