using System.Windows;
using System.Windows.Input;
using ACCcom.Helpers;

namespace ACCcom;

/// <summary>
/// Minimal modal input dialog (used e.g. for renaming quick send pages).
/// </summary>
public partial class PromptDialog : Window
{
    public string InputValue => InputBox.Text;

    private PromptDialog(string title, string message, string initialValue)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    /// <summary>Shows the dialog and returns the entered text, or null when cancelled.</summary>
    public static string? Show(string title, string message, string initialValue = "")
    {
        var dlg = new PromptDialog(title, message, initialValue)
            { Owner = Application.Current.MainWindow };
        return dlg.ShowDialog() == true ? dlg.InputValue : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
        else if (e.Key == Key.Escape) DialogResult = false;
    }
}
