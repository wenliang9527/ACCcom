using System.IO;
using System.Windows;
using System.Windows.Controls;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.Helpers;

namespace ACCcom;

public partial class CompareWindow : Window
{
    public CompareWindow()
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "CompareWindow");
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

            // The row construction (string interpolation + DiffRow allocation per
            // line) is pure computation; run it off the UI thread so comparing
            // 100k-line files doesn't freeze the window. DiffEngine is in Core
            // and unit-tested.
            List<DiffRow> rowsA, rowsB;
            int matching, different;
            try
            {
                (rowsA, rowsB, matching, different) = await Task.Run(() => DiffEngine.BuildDiff(linesA, linesB));
            }
            catch (Exception ex)
            {
                SummaryText.Text = string.Format(LanguageManager.Instance["CompareWindow.ReadFileError"], ex.Message);
                return;
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
