using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

/// <summary>
/// Persists quick-send pages to shortcuts.json (v2 paginated format) and
/// handles export/import files. Legacy v1 flat ShortcutItem arrays are
/// migrated into a single default page on load.
/// </summary>
public class ShortcutManager
{
    public const int FormatVersion = 2;
    public const string DefaultPageName = "默认页";

    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACCcom");

    public static readonly string ShortcutsFile = Path.Combine(BaseDir, "shortcuts.json");

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public class ShortcutStore
    {
        public int Version { get; set; } = FormatVersion;
        public List<ShortcutPage> Pages { get; set; } = new();
    }

    /// <summary>Loads pages from shortcuts.json, migrating the legacy flat format when found.</summary>
    public async Task<List<ShortcutPage>> LoadAsync()
    {
        if (!File.Exists(ShortcutsFile))
            return NewDefaultStore();

        var json = await Task.Run(() => File.ReadAllText(ShortcutsFile)).ConfigureAwait(false);
        var pages = ParsePages(json);
        return pages.Count > 0 ? pages : NewDefaultStore();
    }

    public void Save(IReadOnlyList<ShortcutPage> pages)
    {
        Directory.CreateDirectory(BaseDir);
        var store = new ShortcutStore { Version = FormatVersion, Pages = new List<ShortcutPage>(pages) };
        File.WriteAllText(ShortcutsFile, JsonSerializer.Serialize(store, IndentedOptions));
    }

    /// <summary>Parses either a v2 store or a legacy flat ShortcutItem array.</summary>
    public static List<ShortcutPage> ParsePages(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ShortcutPage>();

        try
        {
            var store = JsonSerializer.Deserialize<ShortcutStore>(json);
            if (store?.Pages is { Count: > 0 })
                return store.Pages;
        }
        catch (JsonException) { /* fall through to legacy parsing */ }

        try
        {
            var legacy = JsonSerializer.Deserialize<List<ShortcutItem>>(json);
            if (legacy is { Count: > 0 })
                return new List<ShortcutPage>
                {
                    new() { Name = DefaultPageName, Commands = new ObservableCollection<ShortcutItem>(legacy) }
                };
        }
        catch (JsonException) { }

        return new List<ShortcutPage>();
    }

    /// <summary>Exports pages to an external file (shareable between machines).</summary>
    public static void ExportToFile(string filePath, IEnumerable<ShortcutPage> pages)
    {
        var store = new ShortcutStore { Version = FormatVersion, Pages = new List<ShortcutPage>(pages) };
        File.WriteAllText(filePath, JsonSerializer.Serialize(store, IndentedOptions));
    }

    /// <summary>Reads pages from an exported file. Returns null when the file is invalid.</summary>
    public static List<ShortcutPage>? ImportFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var pages = ParsePages(File.ReadAllText(filePath));
            return pages.Count > 0 ? pages : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<ShortcutPage> NewDefaultStore()
        => new() { new ShortcutPage { Name = DefaultPageName } };
}
