using System.Windows;
using System.Windows.Controls;
using ACCcom.ViewModels;

namespace ACCcom.Converters;

public class FieldValuesTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DefaultTemplate { get; set; }
    public DataTemplate? EnumTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is FieldItemViewModel vm && (vm.Type == "enum" || vm.Type == "bitfield"))
            return EnumTemplate ?? DefaultTemplate!;
        return DefaultTemplate!;
    }
}
