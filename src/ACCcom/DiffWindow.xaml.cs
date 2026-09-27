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
    // Per-compare brush snapshot. The old properties called FindResource on
    // every access — 4 lookups per byte, so a 4KB compare did ~16k dictionary
    // walks. Resolved once per Compare_Click instead, which keeps DiffWindow
    // following the active palette without paying per byte.
    private SolidColorBrush _matchBrush = null!;
    private SolidColorBrush _diffBrush = null!;
    private SolidColorBrush _matchFg = null!;
    private SolidColorBrush _diffFg = null!;

    // Byte → "XX " with trailing space. 256 interned strings instead of a
    // ToString("X2") + concatenation per byte per side.
    private static readonly string[] HexTriples = BuildHexTriples();

    private static string[] BuildHexTriples()
    {
        var table = new string[256];
        for (int i = 0; i < 256; i++)
            table[i] = i.ToString("X2") + " ";
        return table;
    }

    // Run styles for the merged-inline renderer. Plain inherits the TextBlock's
    // font/foreground (group gaps and alignment padding), the other two carry
    // the theme's diff highlight.
    private const int StylePlain = 0;
    private const int StyleMatch = 1;
    private const int StyleDiff = 2;
    private const int GroupBytes = 8;
    private const string GroupGap = "  ";
    private const string PadTriple = "   ";

    public DiffWindow()
    {
        InitializeComponent();
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

        // Theme brushes resolved once per compare (see field comment).
        _matchBrush = (SolidColorBrush)FindResource("DiffMatchBgBrush");
        _diffBrush = (SolidColorBrush)FindResource("DiffDiffBgBrush");
        _matchFg = (SolidColorBrush)FindResource("StatusGreenBrush");
        _diffFg = (SolidColorBrush)FindResource("StatusErrorBrush");

        // Merge consecutive same-style bytes into one Run: a 4KB compare used
        // to create ~4000 Runs (one per byte per side) each carrying its own
        // font objects; a mostly-matching frame now yields a handful.
        // The TextBlock already sets Consolas/13, so runs just inherit.
        BuildSide(DiffTextA.Inlines, bytesA, bytesB.Length, states);
        BuildSide(DiffTextB.Inlines, bytesB, bytesA.Length, states);

        SummaryText.Text = string.Format(LanguageManager.Instance["DiffWindow.DiffResult"], diffCount, maxLen, bytesA.Length, bytesB.Length);
    }

    private void BuildSide(System.Windows.Documents.InlineCollection inlines, byte[] bytes, int otherLen, ByteDiff.ByteState[] states)
    {
        inlines.Clear();

        var text = new System.Text.StringBuilder();
        int currentStyle = -1;

        void Flush(int style)
        {
            if (text.Length == 0) return;
            var run = new Run(text.ToString());
            switch (style)
            {
                case StyleMatch:
                    run.Background = _matchBrush;
                    run.Foreground = _matchFg;
                    break;
                case StyleDiff:
                    run.Background = _diffBrush;
                    run.Foreground = _diffFg;
                    break;
            }
            inlines.Add(run);
            text.Clear();
        }

        void Emit(int style, string fragment)
        {
            if (style != currentStyle)
            {
                Flush(currentStyle);
                currentStyle = style;
            }
            text.Append(fragment);
        }

        int len = states.Length;
        for (int i = 0; i < len; i++)
        {
            // Group gap every 8 bytes: plain run (inherits TextBlock styling).
            if (i > 0 && i % GroupBytes == 0)
                Emit(StylePlain, GroupGap);

            bool hasSelf = i < bytes.Length;
            bool hasOther = i < otherLen;

            if (!hasSelf)
            {
                // Padding for alignment — plain run.
                Emit(StylePlain, PadTriple);
                continue;
            }

            bool same = states[i] == ByteDiff.ByteState.Match;
            // Extra byte (no counterpart) and mismatch share the diff style.
            int style = hasOther && same ? StyleMatch : StyleDiff;
            Emit(style, HexTriples[bytes[i]]);
        }

        Flush(currentStyle);
    }

}
