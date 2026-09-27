using System.Windows;
using ACCcom.Helpers;
using ACCcom.ViewModels;

namespace ACCcom;

public partial class SchemaEditorWindow : Window
{
    public SchemaEditorWindow(SchemaEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WindowHelper.AttachWindowState(this, "SchemaEditorWindow");
    }

}
