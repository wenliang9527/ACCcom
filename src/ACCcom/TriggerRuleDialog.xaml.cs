using System.Windows;
using System.Windows.Controls;
using ACCcom.Core.Models;
using ACCcom.Helpers;
using TriggerAction = ACCcom.Core.Models.TriggerAction;

namespace ACCcom;

public partial class TriggerRuleDialog : Window
{
    public sealed record NamedItem(string Name, object Value);

    public static readonly IReadOnlyList<NamedItem> MatchModes = new[]
    {
        new NamedItem("Contains", "contains"),
        new NamedItem("Exact", "exact"),
        new NamedItem("Regex", "regex"),
    };

    public static readonly IReadOnlyList<NamedItem> Directions = new[]
    {
        new NamedItem("Both", null!),
        new NamedItem("RX", "RX"),
        new NamedItem("TX", "TX"),
    };

    public static readonly IReadOnlyList<NamedItem> Actions = new[]
    {
        new NamedItem("Send Command", TriggerAction.SendCommand),
        new NamedItem("Save To File", TriggerAction.SaveToFile),
        new NamedItem("Play Sound", TriggerAction.PlaySound),
        new NamedItem("Log Message", TriggerAction.LogMessage),
        new NamedItem("None", TriggerAction.None),
    };

    public TriggerRule Rule { get; private set; }

    public TriggerRuleDialog(TriggerRule rule)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);

        Rule = rule;

        NameBox.Text = rule.Name;
        PatternBox.Text = rule.Pattern;
        MatchModeBox.SelectedValue = rule.MatchMode;
        DirectionBox.SelectedValue = rule.Direction ?? "";
        MatchHexBox.IsChecked = rule.MatchHex;
        ActionBox.SelectedValue = rule.Action;
        ActionParameterBox.Text = rule.ActionParameter ?? "";
        EnabledBox.IsChecked = rule.Enabled;

        UpdateActionParameterLabel();
        UpdateValidation();

        NameBox.TextChanged += (_, _) => UpdateValidation();
        PatternBox.TextChanged += (_, _) => UpdateValidation();
        ActionParameterBox.TextChanged += (_, _) => UpdateValidation();

        NameBox.Focus();
        NameBox.SelectAll();
    }

    /// <summary>The parameter field's meaning changes with the action: a command
    /// to send, a file path, or a log message. PlaySound needs no parameter.</summary>
    private void UpdateActionParameterLabel()
    {
        var action = (TriggerAction)ActionBox.SelectedValue;
        ActionParameterBox.IsEnabled = action is TriggerAction.SendCommand
            or TriggerAction.SaveToFile
            or TriggerAction.LogMessage;
        ActionParameterLabel.Text = action switch
        {
            TriggerAction.SendCommand => LanguageManager.Instance["TriggerDialog.ParamSendCommand"],
            TriggerAction.SaveToFile => LanguageManager.Instance["TriggerDialog.ParamSaveToFile"],
            TriggerAction.LogMessage => LanguageManager.Instance["TriggerDialog.ParamLogMessage"],
            _ => LanguageManager.Instance["TriggerDialog.ActionParameter"]
        };
    }

    private void ActionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionParameterLabel();
        UpdateValidation();
    }

    private void UpdateValidation()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ValidationText.Text = LanguageManager.Instance["TriggerDialog.NameRequired"];
            OkButton.IsEnabled = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(PatternBox.Text))
        {
            ValidationText.Text = LanguageManager.Instance["TriggerDialog.PatternRequired"];
            OkButton.IsEnabled = false;
            return;
        }
        var action = (TriggerAction)ActionBox.SelectedValue;
        if (action is TriggerAction.SendCommand or TriggerAction.SaveToFile
            && string.IsNullOrWhiteSpace(ActionParameterBox.Text))
        {
            ValidationText.Text = LanguageManager.Instance["TriggerDialog.ParameterRequired"];
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
        Rule.MatchMode = (string)MatchModeBox.SelectedValue;
        Rule.Direction = DirectionBox.SelectedValue as string;
        if (string.IsNullOrEmpty(Rule.Direction)) Rule.Direction = null;
        Rule.MatchHex = MatchHexBox.IsChecked == true;
        Rule.Action = (TriggerAction)ActionBox.SelectedValue;
        Rule.ActionParameter = ActionParameterBox.IsEnabled ? ActionParameterBox.Text : null;
        Rule.Enabled = EnabledBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}