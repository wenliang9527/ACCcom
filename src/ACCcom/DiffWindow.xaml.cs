using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ACCcom;

public partial class DiffWindow : Window
{
    public DiffWindow()
    {
        InitializeComponent();
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
            SummaryText.Text = "Please paste hex data into both frames";
            return;
        }

        // Strip spaces and validate hex
        var cleanA = rawA.Replace(" ", "").Replace("\r", "").Replace("\n", "");
        var cleanB = rawB.Replace(" ", "").Replace("\r", "").Replace("\n", "");

        byte[] bytesA, bytesB;
        try
        {
            bytesA = Convert.FromHexString(cleanA);
        }
        catch (FormatException)
        {
            SummaryText.Text = "Frame A contains invalid hex characters";
            return;
        }

        try
        {
            bytesB = Convert.FromHexString(cleanB);
        }
        catch (FormatException)
        {
            SummaryText.Text = "Frame B contains invalid hex characters";
            return;
        }

        // Build inline hex display with highlighting
        DiffTextA.Inlines.Clear();
        DiffTextB.Inlines.Clear();

        int maxLen = Math.Max(bytesA.Length, bytesB.Length);
        int diffCount = 0;

        var matchBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x22, 0xC5, 0x5E));
        var diffBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));
        var matchFg = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        var diffFg = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
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
            bool same = hasA && hasB && bytesA[i] == bytesB[i];

            if (!same) diffCount++;

            // Frame A byte
            if (hasA)
            {
                string hex = bytesA[i].ToString("X2");
                var run = new Run(hex + " ") { FontSize = 13, FontFamily = new FontFamily("Consolas") };
                if (!hasB)
                {
                    // Extra byte in A
                    run.Background = diffBrush;
                    run.Foreground = diffFg;
                }
                else if (same)
                {
                    run.Background = matchBrush;
                    run.Foreground = matchFg;
                }
                else
                {
                    run.Background = diffBrush;
                    run.Foreground = diffFg;
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
                    run.Background = diffBrush;
                    run.Foreground = diffFg;
                }
                else if (same)
                {
                    run.Background = matchBrush;
                    run.Foreground = matchFg;
                }
                else
                {
                    run.Background = diffBrush;
                    run.Foreground = diffFg;
                }
                DiffTextB.Inlines.Add(run);
            }
            else
            {
                // Padding for alignment
                DiffTextB.Inlines.Add(new Run("   ") { FontSize = 13, FontFamily = new FontFamily("Consolas") });
            }
        }

        SummaryText.Text = $"{diffCount} bytes differ out of {maxLen} total  |  A: {bytesA.Length} bytes  B: {bytesB.Length} bytes";
    }
}
