using System.Text.Json;

namespace ACCcom.Core.Services;

public abstract class JsonFilePersistenceManager<T>
{
    protected static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACCcom");

    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    protected abstract string FileName { get; }

    protected virtual List<T> DefaultValue() => new();

    public async Task<List<T>> LoadAsync()
    {
        var path = Path.Combine(BaseDir, FileName);
        if (!File.Exists(path))
            return DefaultValue();

        var json = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(false);
        var items = JsonSerializer.Deserialize<T[]>(json);
        return items != null ? new List<T>(items) : DefaultValue();
    }

    public void Save(IReadOnlyList<T> items)
    {
        Directory.CreateDirectory(BaseDir);
        var path = Path.Combine(BaseDir, FileName);
        var json = JsonSerializer.Serialize(items.ToArray(), IndentedOptions);
        File.WriteAllText(path, json);
    }

    public T[] LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<T[]>(json) ?? Array.Empty<T>();
    }
}
