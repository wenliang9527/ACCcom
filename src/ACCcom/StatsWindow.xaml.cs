using System.Windows;
using ACCcom.Helpers;

namespace ACCcom;

public partial class StatsWindow : Window
{
    public StatsWindow()
    {
        InitializeComponent();
        WindowHelper.AttachWindowState(this, "StatsWindow");
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        // Flatten the visible StatsViewModel properties as "label: value" lines
        // so a user can paste the whole snapshot into an issue or chat.
        if (DataContext is not ViewModels.StatsViewModel vm) return;
        var lines = new List<string>
        {
            LanguageManager.Instance["Stats.RXSection"],
            $"{LanguageManager.Instance["Stats.BytesPerSec"]}: {vm.RxBytesPerSec}",
            $"{LanguageManager.Instance["Stats.FramesPerSec"]}: {vm.RxFramesPerSec}",
            $"{LanguageManager.Instance["Stats.TotalBytes"]}: {vm.TotalRxBytes}",
            $"{LanguageManager.Instance["Stats.TotalFrames"]}: {vm.TotalRxFrames}",
            "",
            LanguageManager.Instance["Stats.TXSection"],
            $"{LanguageManager.Instance["Stats.BytesPerSec"]}: {vm.TxBytesPerSec}",
            $"{LanguageManager.Instance["Stats.FramesPerSec"]}: {vm.TxFramesPerSec}",
            $"{LanguageManager.Instance["Stats.TotalBytes"]}: {vm.TotalTxBytes}",
            $"{LanguageManager.Instance["Stats.TotalFrames"]}: {vm.TotalTxFrames}",
            "",
            LanguageManager.Instance["Stats.Connection"],
            $"{LanguageManager.Instance["Stats.Duration"]}: {vm.ConnectionDuration}",
            $"{LanguageManager.Instance["Stats.ErrorRate"]}: {vm.ErrorRate} %",
            $"{LanguageManager.Instance["Stats.AvgFrameInterval"]}: {vm.AvgFrameInterval} ms",
        };
        try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy */ }
    }
}
