using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ACCcom.Core.Models;
using ACCcom.Core.Services;
using ACCcom.Helpers;

namespace ACCcom;

public partial class DiffWindow : Window
{
    // Match/diff highlight brushes are resolved from the active theme on every
    // compare so DiffWindow follows the current palette (light/dark/art themes).
    private SolidColorBrush MatchBrush => (SolidColorBrush)FindResource("DiffMatchBgBrush");
    private SolidColorBrush DiffBrush => (SolidColorBrush)FindResource("DiffDiffBgBrush");
    private SolidColorBrush MatchFg => (SolidColorBrush)FindResource("StatusGreenBrush");
    private SolidColorBrush DiffFg => (SolidColorBrush)FindResource("StatusErrorBrush");

    public DiffWindow()
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "DiffWindow");
    }

    /// <summary>
    /// Pre-fill hex values (e.g. from selected log entries).
    /// </summary>
    public DiffWindow(string hexA, string hexB) : this()
    {
        HexBoxA.Text = hexA;
        HexBoxB.Text = hexB;
        Compare_Click(this, new RoutedEventArgs());
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var rawA = HexBoxA.Text?.Trim() ?? "";
        var rawB = HexBoxB.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(rawA) || string.IsNullOrEmpty(rawB))
        {
            SummaryText.Text = LanguageManager.Instance["DiffWindow.PleasePaste"];
            return;
        }

        // Strip spaces and validate hex (HexHelper also accepts tabs/newlines
        // and rejects odd digit counts / invalid chars, matching the send box).
        if (!HexHelper.TryHexStringToBytes(rawA, out var bytesA))
        {
            SummaryText.Text = LanguageManager.Instance["DiffWindow.InvalidHexA"];
            return;
        }

        if (!HexHelper.TryHexStringToBytes(rawB, out var bytesB))
        {
            SummaryText.Text = LanguageManager.Instance["DiffWindow.InvalidHexB"];
            return;
        }

        // Positional byte comparison (classification + diff count) in Core.
        var (states, diffCount) = ByteDiff.Compare(bytesA, bytesB);
        int maxLen = states.Length;

        // Build inline hex display with highlighting
        DiffTextA.Inlines.Clear();
        DiffTextB.Inlines.Clear();

        var dimFg = (SolidColorBrush)FindResource("InkTertiaryBrush");

        for (int i = 0; i < maxLen; i++)
        {
            // Add space every 8 bytes for readability
            if (i > 0 && i % 8 == 0)
            {
                DiffTextA.Inlines.Add(new Run("  ") { FontSize = 13 });
                DiffTextB.Inlines.Add(new Run("  ") { FontSize = 13 });
            }

            bool hasA = i < bytesA.Length;
            bool hasB = i < bytesB.Length;
            bool same = states[i] == ByteDiff.ByteState.Match;

            // Frame A byte
            if (hasA)
            {
                string hex = bytesA[i].ToString("X2");
                var run = new Run(hex + " ") { FontSize = 13, FontFamily = new FontFamily("Consolas") };
                if (!hasB)
                {
                    // Extra byte in A
                    run.Background = DiffBrush;
                    run.Foreground = DiffFg;
                }
                else if (same)
                {
                    run.Background = MatchBrush;
                    run.Foreground = MatchFg;
                }
                else
                {
                    run.Background = DiffBrush;
                    run.Foreground = DiffFg;
                }
                DiffTextA.Inlines.Add(run);
            }
            else
            {
                // Padding for alignment
                DiffTextA.Inlines.Add(new Run("   ") { FontSize = 13, FontFamily = new FontFamily("Consolas") });
            }

            // Frame B byte
            if (hasB)
            {
                string hex = bytesB[i].ToString("X2");
                var run = new Run(hex + " ") { FontSize = 13, FontFamily = new FontFamily("Consolas") };
                if (!hasA)
                {
                    // Extra byte in B
                    run.Background = DiffBrush;
                    run.Foreground = DiffFg;
                }
                else if (same)
                {
                    run.Background = MatchBrush;
                    run.Foreground = MatchFg;
                }
                else
                {
                    run.Background = DiffBrush;
                    run.Foreground = DiffFg;
                }
                DiffTextB.Inlines.Add(run);
            }
            else
            {
                // Padding for alignment
                DiffTextB.Inlines.Add(new Run("   ") { FontSize = 13, FontFamily = new FontFamily("Consolas") });
            }
        }

        SummaryText.Text = string.Format(LanguageManager.Instance["DiffWindow.DiffResult"], diffCount, maxLen, bytesA.Length, bytesB.Length);
    }

    private void TitleBarMin_Click(object sender, RoutedEventArgs e) => WindowHelper.Minimize(this);
    private void TitleBarMax_Click(object sender, RoutedEventArgs e) => WindowHelper.MaximizeRestore(this);
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
