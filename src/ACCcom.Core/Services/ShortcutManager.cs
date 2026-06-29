using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ShortcutManager : JsonFilePersistenceManager<ShortcutItem>
{
    public static readonly string ShortcutsFile = Path.Combine(BaseDir, "shortcuts.json");
    protected override string FileName => "shortcuts.json";

    public static List<ShortcutItem> GetDefaults()
    {
        return new List<ShortcutItem>();
    }
}
