using System.Windows;
using ACCcom.Core.Models;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class TriggerWindow : Window
{
    private readonly TriggerViewModel _vm;

    public TriggerWindow(TriggerViewModel vm)
    {
        InitializeComponent();
        WindowHelper.AttachWindowState(this, "TriggerWindow");
        _vm = vm;
        DataContext = vm;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is TriggerRule rule)
            _vm.OpenEditDialog(rule);
    }

    private void Row_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesGrid.SelectedItem is TriggerRule rule)
            _vm.OpenEditDialog(rule);
    }
}
