using System.Windows;
using System.Windows.Controls;
using ACCcom.Core.Services;

namespace ACCcom;

public partial class FrameAssemblerConfigWindow : Window
{
    private readonly FrameAssemblerConfig _config;

    public FrameAssemblerConfigWindow(FrameAssemblerConfig config)
    {
        InitializeComponent();
        _config = config;

        EnabledCheckBox.IsChecked = config.Enabled;
        HeaderBox.Text = config.Header;
        OffsetBox.Text = config.LengthFieldOffset.ToString();
        MaxFrameBox.Text = config.MaxFrameSize.ToString();
        TimeoutBox.Text = config.PartialFrameTimeoutMs.ToString();

        foreach (ComboBoxItem item in SizeCombo.Items)
        {
            if ((int)item.Tag == config.LengthFieldSize)
            {
                SizeCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _config.Enabled = EnabledCheckBox.IsChecked ?? false;
        _config.Header = HeaderBox.Text.Trim();

        if (int.TryParse(OffsetBox.Text.Trim(), out var offset))
            _config.LengthFieldOffset = offset;

        if (SizeCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out var size))
            _config.LengthFieldSize = size;

        if (int.TryParse(MaxFrameBox.Text.Trim(), out var max))
            _config.MaxFrameSize = max;

        if (int.TryParse(TimeoutBox.Text.Trim(), out var timeout))
            _config.PartialFrameTimeoutMs = timeout;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
