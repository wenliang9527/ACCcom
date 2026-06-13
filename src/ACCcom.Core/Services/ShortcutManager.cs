using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ShortcutManager : JsonFilePersistenceManager<ShortcutItem>
{
    public static readonly string ShortcutsFile = Path.Combine(AppContext.BaseDirectory, "shortcuts.json");
    protected override string FileName => "shortcuts.json";

    public static List<ShortcutItem> GetDefaults()
    {
        return new List<ShortcutItem>
        {
            new() { Name = "查询版本", Command = "AT+GMR" },
            new() { Name = "重启", Command = "AT+RST" },
            new() { Name = "查询网络", Command = "AT+CGATT?" }
        };
    }
}
