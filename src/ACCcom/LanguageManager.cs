using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace ACCcom;

public class LanguageManager : INotifyPropertyChanged
{
    private static readonly LanguageManager _instance = new();
    public static LanguageManager Instance => _instance;

    private Dictionary<string, string> _strings = new();
    private string _currentLanguage = "zh-CN";

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                LoadLanguage(value);
                OnPropertyChanged();
                // Notify all indexed bindings to refresh
                OnPropertyChanged("Item");
                OnPropertyChanged("Item[]");
            }
        }
    }

    /// <summary>
    /// Indexer for XAML binding: {Binding [key], Source={x:Static local:LanguageManager.Instance}}
    /// </summary>
    public string this[string key] => _strings.TryGetValue(key, out var v) ? v : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadLanguage(string langCode)
    {
        _currentLanguage = langCode;
        var basePath = Path.Combine(AppContext.BaseDirectory, "Languages");
        var filePath = Path.Combine(basePath, $"{langCode}.json");

        if (File.Exists(filePath))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                    _strings = dict;
            }
            catch
            {
                // Fall back to empty — indexer returns key
            }
        }

        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
