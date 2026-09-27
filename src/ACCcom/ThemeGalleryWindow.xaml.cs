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
        WindowHelper.AttachWindowState(this, "ThemeGalleryWindow");
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

}
