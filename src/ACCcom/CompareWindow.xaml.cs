using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ACCcom;

public partial class CompareWindow : Window
{
    private static readonly SolidColorBrush DiffHighlightBrush = new(Color.FromArgb(40, 255, 200, 0));

    public CompareWindow()
    {
        InitializeComponent();
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

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FileAPath.Text) || string.IsNullOrEmpty(FileBPath.Text))
        {
            SummaryText.Text = LanguageManager.Instance["CompareWindow.SelectFilesError"];
            return;
        }

        string[] linesA, linesB;
        try
        {
            linesA = File.ReadAllLines(FileAPath.Text);
            linesB = File.ReadAllLines(FileBPath.Text);
        }
        catch (IOException ex)
        {
            SummaryText.Text = string.Format(LanguageManager.Instance["CompareWindow.ReadFileError"], ex.Message);
            return;
        }

        ListBoxA.Items.Clear();
        ListBoxB.Items.Clear();

        int maxCount = Math.Max(linesA.Length, linesB.Length);
        int matching = 0, different = 0;

        for (int i = 0; i < maxCount; i++)
        {
            var a = i < linesA.Length ? linesA[i] : "";
            var b = i < linesB.Length ? linesB[i] : "";

            bool same = string.Equals(a, b, StringComparison.Ordinal);
            if (same) matching++; else different++;

            var itemA = new ListBoxItem { Content = $"[{i + 1}] {a}", Padding = new Thickness(4, 1, 4, 1) };
            var itemB = new ListBoxItem { Content = $"[{i + 1}] {b}", Padding = new Thickness(4, 1, 4, 1) };

            if (!same)
            {
                itemA.Background = DiffHighlightBrush;
                itemB.Background = DiffHighlightBrush;
            }

            ListBoxA.Items.Add(itemA);
            ListBoxB.Items.Add(itemB);
        }

        SummaryText.Text = string.Format(LanguageManager.Instance["CompareWindow.SummaryFormat"], maxCount, matching, different);
    }
}
