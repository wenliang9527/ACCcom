using System.Windows;
using System.Windows.Controls;
using ACCcom.Core.Services;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class HighlightWindow : Window
{
    private readonly HighlightViewModel _vm;

    public HighlightWindow(HighlightViewModel vm)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        _vm = vm;
        DataContext = vm;
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is HighlightRule rule)
            new HighlightRuleDialog(rule) { Owner = this }.ShowDialog();
    }

    private void Row_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesGrid.SelectedItem is HighlightRule rule)
            new HighlightRuleDialog(rule) { Owner = this }.ShowDialog();
    }
}