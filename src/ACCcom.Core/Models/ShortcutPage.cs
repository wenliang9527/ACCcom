using System.Collections.ObjectModel;

namespace ACCcom.Core.Models;

/// <summary>A named page of quick-send commands shown in the quick send sidebar.</summary>
public class ShortcutPage
{
    public string Name { get; set; } = "";
    public ObservableCollection<ShortcutItem> Commands { get; set; } = new();
}
