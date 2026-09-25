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

        // ListBox doesn't expose ScrollChanged (it lives on the inner ScrollViewer);
        // bubble-phase handler catches scrolls from either list.
        ListBoxA.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ListBoxA_ScrollChanged));
        ListBoxB.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ListBoxB_ScrollChanged));
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
            SyncScrollFromA = false;
            SyncScrollFromB = false;

            // Index of every differing row (same index in both lists by construction).
            _diffIndices.Clear();
            for (int i = 0; i < rowsA.Count; i++)
                if (rowsA[i].IsDiff || (i < rowsB.Count && rowsB[i].IsDiff))
                    _diffIndices.Add(i);
            _diffCursor = -1;
            PrevDiffButton.IsEnabled = _diffIndices.Count > 0;
            NextDiffButton.IsEnabled = _diffIndices.Count > 0;

            SummaryText.Text = _diffIndices.Count == 0
                ? string.Format(LanguageManager.Instance["CompareWindow.SummaryFormat"], maxCount, matching, different)
                    + " · " + LanguageManager.Instance["CompareWindow.NoDiff"]
                : string.Format(LanguageManager.Instance["CompareWindow.SummaryFormat"], maxCount, matching, different);
        }
        finally
        {
            CompareButton.IsEnabled = true;
        }
    }

    private bool SyncScrollFromA;
    private bool SyncScrollFromB;
    private ScrollViewer? _scrollA;
    private ScrollViewer? _scrollB;
    private readonly List<int> _diffIndices = new();
    private int _diffCursor = -1;

    private void NextDiff_Click(object sender, RoutedEventArgs e) => JumpToDiff(+1);
    private void PrevDiff_Click(object sender, RoutedEventArgs e) => JumpToDiff(-1);

    private void JumpToDiff(int step)
    {
        if (_diffIndices.Count == 0)
        {
            SummaryText.Text = LanguageManager.Instance["CompareWindow.NoDiff"];
            return;
        }

        // Advance with wrap-around; first press from -1 lands on index 0 for
        // "next" and the last entry for "prev".
        if (_diffCursor < 0)
            _diffCursor = step > 0 ? 0 : _diffIndices.Count - 1;
        else
            _diffCursor = (_diffCursor + step + _diffIndices.Count) % _diffIndices.Count;

        var index = _diffIndices[_diffCursor];
        SelectAndReveal(ListBoxA, index);
        SelectAndReveal(ListBoxB, index);
        // Keep both lists' scroll positions equal so the selection stays aligned.
        SyncScrollFromA = false;
        SyncScrollFromB = false;
    }

    private static void SelectAndReveal(ListBox list, int index)
    {
        if (index < 0 || index >= list.Items.Count) return;
        list.SelectedIndex = index;
        list.UpdateLayout();
        list.ScrollIntoView(list.Items[index]);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private void ListBoxA_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (SyncScrollFromB || ListBoxB.Items.Count == 0 || e.VerticalChange == 0) return;
        _scrollA ??= FindScrollViewer(ListBoxA);
        _scrollB ??= FindScrollViewer(ListBoxB);
        if (_scrollA == null || _scrollB == null) return;
        SyncScrollFromA = true;
        try
        {
            _scrollB.ScrollToVerticalOffset(_scrollA.VerticalOffset);
        }
        finally
        {
            SyncScrollFromA = false;
        }
    }

    private void ListBoxB_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (SyncScrollFromA || ListBoxA.Items.Count == 0 || e.VerticalChange == 0) return;
        _scrollA ??= FindScrollViewer(ListBoxA);
        _scrollB ??= FindScrollViewer(ListBoxB);
        if (_scrollA == null || _scrollB == null) return;
        SyncScrollFromB = true;
        try
        {
            _scrollA.ScrollToVerticalOffset(_scrollB.VerticalOffset);
        }
        finally
        {
            SyncScrollFromB = false;
        }
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
