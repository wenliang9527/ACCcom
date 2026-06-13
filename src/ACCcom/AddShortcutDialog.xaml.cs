using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace ACCcom;

public partial class AddShortcutDialog : Window
{
    private static readonly Regex HexPattern = new(@"^[0-9A-Fa-f\s]+$", RegexOptions.Compiled);
    private bool _updating;

    public string ShortcutName => NameBox.Text;
    public string ShortcutCommand => CommandBox.Text;
    public bool ShortcutIsHex => HexCheckBox.IsChecked == true;

    public AddShortcutDialog()
    {
        _updating = true;
        InitializeComponent();
        _updating = false;
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
            // 自动格式化为带空格的 HEX 显示
            _updating = true;
            var caret = CommandBox.CaretIndex;
            var formatted = string.Join(" ", Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2)).ToArray());
            if (formatted != text)
            {
                CommandBox.Text = formatted;
                // 调整光标位置
                var spacesBeforeCaret = formatted.Take(caret).Count(c => c == ' ');
                CommandBox.CaretIndex = Math.Min(caret + spacesBeforeCaret, formatted.Length);
            }
            _updating = false;
            HexInfo.Text = $"{raw.Length / 2} 字节";
        }
        else if (HexPattern.IsMatch(raw))
        {
            HexInfo.Text = "字节数不完整";
        }
        else
        {
            HexInfo.Text = "包含非 HEX 字符";
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
                    CommandBox.Text = string.Join(" ", Enumerable.Range(0, raw.Length / 2).Select(i => raw.Substring(i * 2, 2)));
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
            MessageBox.Show("名称和命令不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
