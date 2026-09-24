using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class ThemeGalleryWindow : Window
{
    public ThemeGalleryViewModel ViewModel { get; }

    public ThemeGalleryWindow(ThemeGalleryViewModel viewModel)
    {
        InitializeComponent();
        WindowHelper.SetupTitleBar(this, TitleBar);
        WindowHelper.AttachWindowState(this, "ThemeGalleryWindow");
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void TitleBarClose_Click(object sender, RoutedEventArgs e) => Close();
}
