using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ACCcom.Core.Services;
using ACCcom.Helpers;

namespace ACCcom;

public partial class HighlightRuleDialog : Window
{
    /// <summary>ComboBox-friendly wrappers around the enums. Bound by name so
    /// we don't need a XAML-side enum-to-string converter.</summary>
    public sealed record NamedItem(string Name, object Value);

    public static readonly IReadOnlyList<NamedItem> MatchTypes = new[]
    {
        new NamedItem("Contains", HighlightMatchType.Contains),
        new NamedItem("Exact", HighlightMatchType.Exact),
        new NamedItem("Regex", HighlightMatchType.Regex),
    };

    public static readonly IReadOnlyList<NamedItem> Directions = new[]
    {
        new NamedItem("Both", null!),
        new NamedItem("RX", HighlightDirection.RX),
        new NamedItem("TX", HighlightDirection.TX),
    };

    public static readonly IReadOnlyList<NamedItem> ColorPresets = new[]
    {
        new NamedItem("Red (#FF6B6B)",    "#FF6B6B"),
        new NamedItem("Orange (#FF9F43)", "#FF9F43"),
        new NamedItem("Yellow (#FECA57)", "#FECA57"),
        new NamedItem("Green (#10AC84)",  "#10AC84"),
        new NamedItem("Blue (#5F7CFA)",   "#5F7CFA"),
        new NamedItem("Pink (#FF6BCB)",   "#FF6BCB"),
        new NamedItem("Magenta (#C4456B)","#C4456B"),
        new NamedItem("Gray (#95A5A6)",   "#95A5A6"),
    };

    public HighlightRule Rule { get; private set; }

    public HighlightRuleDialog(HighlightRule rule)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);

        Rule = rule;

        NameBox.Text = rule.Name;
        PatternBox.Text = rule.Pattern;
        MatchTypeBox.SelectedValue = rule.MatchType;
        DirectionBox.SelectedValue = (object?)rule.Direction ?? MatchTypes[0];
        MatchHexBox.IsChecked = rule.MatchHex;
        ColorBox.SelectedValue = rule.Color;
        PriorityBox.Text = rule.Priority.ToString(CultureInfo.InvariantCulture);
        EnabledBox.IsChecked = rule.IsEnabled;

        UpdateColorSwatch();
        UpdateValidation();
        ColorBox.SelectionChanged += (_, _) => { UpdateColorSwatch(); UpdateValidation(); };
        NameBox.TextChanged += (_, _) => UpdateValidation();
        PatternBox.TextChanged += (_, _) => UpdateValidation();
        PriorityBox.TextChanged += (_, _) => UpdateValidation();

        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void UpdateColorSwatch()
    {
        var hex = ColorBox.SelectedValue as string ?? "#FF6B6B";
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            ColorSwatch.Background = brush;
        }
        catch
        {
            ColorSwatch.Background = Brushes.Gray;
        }
    }

    private void UpdateValidation()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationText.Text = LanguageManager.Instance["HighlightDialog.NameRequired"];
            OkButton.IsEnabled = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(PatternBox.Text))
        {
            ValidationText.Text = LanguageManager.Instance["HighlightDialog.PatternRequired"];
            OkButton.IsEnabled = false;
            return;
        }
        if (!int.TryParse(PriorityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            ValidationText.Text = LanguageManager.Instance["HighlightDialog.PriorityInvalid"];
            OkButton.IsEnabled = false;
            return;
        }
        ValidationText.Text = "";
        OkButton.IsEnabled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!OkButton.IsEnabled) return;
        Rule.Name = NameBox.Text.Trim();
        Rule.Pattern = PatternBox.Text;
        Rule.MatchType = (HighlightMatchType)MatchTypeBox.SelectedValue;
        Rule.Direction = (HighlightDirection?)DirectionBox.SelectedValue;
        Rule.MatchHex = MatchHexBox.IsChecked == true;
        Rule.Color = (string)(ColorBox.SelectedValue ?? "#FF6B6B");
        Rule.Priority = int.Parse(PriorityBox.Text, CultureInfo.InvariantCulture);
        Rule.IsEnabled = EnabledBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}