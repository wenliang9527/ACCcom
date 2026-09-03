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
                vm.StatusText = string.Format(LanguageManager.Instance["StatusBar.Copied"], vm.HttpUrl);
            }
            catch
            {
                // Clipboard access can fail under remote/desktop sessions; ignore silently.
            }
        }
    }

    /// <summary>Double-click on the status text clears it. A plain click passes
    /// through untouched so accidental single clicks don't wipe the message.</summary>
    private void OnStatusClearClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (DataContext is MainViewModel vm)
        {
            vm.StatusText = "";
        }
    }

    /// <summary>Click on the REC chip toggles recording. The chip is only visible
    /// while recording, so a click always means "stop".</summary>
    private void OnRecordingClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ToggleRecordingCommand.CanExecute(null))
        {
            vm.ToggleRecordingCommand.Execute(null);
        }
    }

    private void OnShortcutsClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OpenShortcutsCommand.Execute(null);
        }
    }

    /// <summary>Context-menu item on the REC indicator: reveal the recordings
    /// folder in Explorer. Uses a Click handler (not a Command binding) because
    /// ContextMenus live in a separate namescope and can't see the panel's
    /// DataContext without a PlacementTarget binding.</summary>
    private void OnOpenRecordingsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.OpenRecordingsFolderCommand.Execute(null);
        }
    }
}
