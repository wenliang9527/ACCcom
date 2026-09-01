using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ACCcom.ViewModels;

namespace ACCcom.Controls;

public partial class StatusBarPanel : UserControl
{
    public StatusBarPanel()
    {
        InitializeComponent();
    }

    private void OnCounterClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.DataFlow?.ResetCountersCommand is ICommand cmd && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnHttpUrlClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.HttpUrl))
        {
            try
            {
                Clipboard.SetText(vm.HttpUrl);
                vm.StatusText = $"已复制: {vm.HttpUrl}";
            }
            catch
            {
                // Clipboard access can fail under remote/desktop sessions; ignore silently.
            }
        }
    }
}
