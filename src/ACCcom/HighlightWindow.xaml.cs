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
        WindowHelper.AttachWindowState(this, "HighlightWindow");
        _vm = vm;
        DataContext = vm;
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is HighlightRule rule)
            EditRule(rule);
    }

    private void Row_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesGrid.SelectedItem is HighlightRule rule)
            EditRule(rule);
    }

    /// <summary>The rule dialog mutates the rule in place, which never raises
    /// CollectionChanged — route the result through the VM so the edit persists
    /// to disk and buffered entries get recolored.</summary>
    private void EditRule(HighlightRule rule)
    {
        var dialog = new HighlightRuleDialog(rule) { Owner = this };
        if (dialog.ShowDialog() == true)
            _vm.ApplyEditedRule(rule);
    }
}