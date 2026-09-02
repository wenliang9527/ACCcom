using System.IO;
using System.Windows;
using System.Windows.Controls;
using ACCcom.Helpers;

namespace ACCcom;

public partial class CompareWindow : Window
{
    /// <summary>One rendered line of a compared file.</summary>
    public sealed record DiffRow(string Display, bool IsDiff);

    public CompareWindow()
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
    }

    private void BrowseFileA_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = LanguageManager.Instance["CompareWindow.FileFilter"] };
        if (dlg.ShowDialog() == true) FileAPath.Text = dlg.FileName;
    }

    private void BrowseFileB_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = LanguageManager.Instance["CompareWindow.FileFilter"] };
        if (dlg.ShowDialog() == true) FileBPath.Text = dlg.FileName;
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FileAPath.Text) || string.IsNullOrEmpty(FileBPath.Text))
        {
            SummaryText.Text = LanguageManager.Instance["CompareWindow.SelectFilesError"];
            return;
        }

        CompareButton.IsEnabled = false;
        try
        {
            var pathA = FileAPath.Text;
            var pathB = FileBPath.Text;

            string[] linesA, linesB;
            try
            {
                // Large log files: keep the UI responsive by reading off-thread.
                linesA = await Task.Run(() => File.ReadAllLines(pathA));
                linesB = await Task.Run(() => File.ReadAllLines(pathB));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SummaryText.Text = string.Format(LanguageManager.Instance["CompareWindow.ReadFileError"], ex.Message);
                return;
            }

            int maxCount = Math.Max(linesA.Length, linesB.Length);
            var rowsA = new List<DiffRow>(maxCount);
            var rowsB = new List<DiffRow>(maxCount);
            int matching = 0, different = 0;

            for (int i = 0; i < maxCount; i++)
            {
                var a = i < linesA.Length ? linesA[i] : "";
                var b = i < linesB.Length ? linesB[i] : "";

                bool same = string.Equals(a, b, StringComparison.Ordinal);
                if (same) matching++; else different++;

                rowsA.Add(new DiffRow($"[{i + 1}] {a}", !same));
                rowsB.Add(new DiffRow($"[{i + 1}] {b}", !same));
            }

            // Single ItemsSource assignment; the ListBox virtualizes containers.
            ListBoxA.ItemsSource = rowsA;
            ListBoxB.ItemsSource = rowsB;

            SummaryText.Text = string.Format(LanguageManager.Instance["CompareWindow.SummaryFormat"], maxCount, matching, different);
        }
        finally
        {
            CompareButton.IsEnabled = true;
        }
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
