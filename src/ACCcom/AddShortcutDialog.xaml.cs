using System.Text.RegularExpressions;
using System.Windows;
using ACCcom.Helpers;

namespace ACCcom;

public partial class AddShortcutDialog : Window
{
    private static readonly Regex HexPattern = new(@"^[0-9A-Fa-f\s]+$", RegexOptions.Compiled);
    private bool _updating;

    public string ShortcutName => NameBox.Text;
    public string ShortcutCommand => CommandBox.Text;
    public bool ShortcutIsHex => HexCheckBox.IsChecked == true;

    public AddShortcutDialog() : this("", "", false, isEdit: false) { }

    /// <summary>
    /// Creates the dialog prefilled for editing an existing quick command.
    /// </summary>
    public AddShortcutDialog(string name, string command, bool isHex, bool isEdit)
    {
        _updating = true;
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);

        NameBox.Text = string.IsNullOrEmpty(name) ? LanguageManager.Instance["AddShortcut.DefaultName"] : name;
        CommandBox.Text = command;
        HexCheckBox.IsChecked = isHex;

        if (isEdit)
        {
            TitleText.Text = LanguageManager.Instance["AddShortcut.EditTitle"];
            OkButton.Content = LanguageManager.Instance["AddShortcut.Save"];
        }

        _updating = false;
        if (isEdit)
            CommandBox_TextChanged(CommandBox, null!);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void CommandBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updating) return;
        if (HexCheckBox.IsChecked != true) return;

        var text = CommandBox.Text;
        if (string.IsNullOrWhiteSpace(text)) { HexInfo.Text = ""; return; }

        // 去除空格后检测是否为合法 HEX
        var raw = text.Replace(" ", "");
        if (HexPattern.IsMatch(raw) && raw.Length % 2 == 0)
        {
            _updating = true;
            var caret = CommandBox.CaretIndex;
            var formatted = FormatHexWithSpaces(raw);
            if (formatted != text)
            {
                CommandBox.Text = formatted;
                int spacesBeforeCaret = 0;
                int pos = Math.Min(caret, formatted.Length);
                for (int i = 0; i < pos; i++)
                    if (formatted[i] == ' ') spacesBeforeCaret++;
                CommandBox.CaretIndex = Math.Min(caret + spacesBeforeCaret, formatted.Length);
            }
            _updating = false;
            HexInfo.Text = string.Format(LanguageManager.Instance["AddShortcut.HexBytes"], raw.Length / 2);
        }
        else if (HexPattern.IsMatch(raw))
        {
            HexInfo.Text = LanguageManager.Instance["AddShortcut.HexIncomplete"];
        }
        else
        {
            HexInfo.Text = LanguageManager.Instance["AddShortcut.HexInvalid"];
        }
    }

    private void HexCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (HexCheckBox.IsChecked == true)
        {
            // 勾选 HEX 时，尝试将当前命令转为 HEX 格式
            var text = CommandBox.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var raw = text.Replace(" ", "");
                if (HexPattern.IsMatch(raw) && raw.Length % 2 == 0)
                {
                    _updating = true;
                    CommandBox.Text = FormatHexWithSpaces(raw);
                    _updating = false;
                }
            }
        }
        else
        {
            HexInfo.Text = "";
        }
        CommandBox_TextChanged(sender, null!);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ShortcutName) || string.IsNullOrWhiteSpace(ShortcutCommand))
        {
            MessageBox.Show(LanguageManager.Instance["AddShortcut.ValidationError"], LanguageManager.Instance["AddShortcut.ValidationTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string FormatHexWithSpaces(string raw)
    {
        if (raw.Length == 0) return "";
        int len = raw.Length / 2;
        return string.Create(raw.Length + len - 1, (raw, len), static (span, state) =>
        {
            var (r, _) = state;
            int si = 0;
            for (int i = 0; i < state.len; i++)
            {
                if (i > 0) span[si++] = ' ';
                span[si++] = r[i * 2];
                span[si++] = r[i * 2 + 1];
            }
        });
    }
}
