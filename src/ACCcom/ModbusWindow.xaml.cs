using System.Windows;
using System.Windows.Controls;
using ACCcom.Helpers;
using ACCcom.ViewModels;
using Microsoft.Web.WebView2.Wpf;

namespace ACCcom;

public partial class ModbusWindow : Window
{
    private readonly ModbusViewModel _vm;
    private bool _dashboardInitialized;

    public ModbusWindow(ModbusViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "ModbusWindow");
        MainTabControl.SelectionChanged += OnTabSelected;
    }

    private void OnTabSelected(object sender, SelectionChangedEventArgs e)
    {
        _ = OnTabSelectedAsync(e);
    }

    private async Task OnTabSelectedAsync(SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        var tab = e.AddedItems[0] as TabItem;
        if (tab?.Header?.ToString() != LanguageManager.Instance["Modbus.TabDashboard"]) return;
        if (_dashboardInitialized) return;
        _dashboardInitialized = true;

        MainTabControl.SelectionChanged -= OnTabSelected;

        try
        {
            var wv2 = new WebView2();
            await wv2.EnsureCoreWebView2Async();
            wv2.Source = new Uri("http://localhost:8899/dashboard/");

            DashboardPlaceholder.Visibility = Visibility.Collapsed;
            DashboardContainer.Children.Add(wv2);
        }
        catch (Exception ex)
        {
            DashboardPlaceholder.Text = string.Format(LanguageManager.Instance["Modbus.WebView2Error"], ex.Message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void ScanResultsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ScanResultsList.SelectedItem is ScanResultItem item)
            _vm.UseScanSlaveCommand.Execute(item);
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}