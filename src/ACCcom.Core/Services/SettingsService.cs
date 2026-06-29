using System.Text.Json;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class SettingsService
{
    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ACCcom");

    private static readonly string DefaultSettingsPath =
        Path.Combine(BaseDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private string? _lastError;

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? DefaultSettingsPath;
    }

    public string? LastError => _lastError;

    public AppSettings Load()
    {
        _lastError = null;
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (JsonException ex)
        {
            _lastError = $"Settings file corrupted: {ex.Message}";
        }
        catch (IOException ex)
        {
            _lastError = $"Failed to read settings file: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _lastError = $"Access denied to settings file: {ex.Message}";
        }
        return new AppSettings();
    }

    public bool Save(AppSettings settings)
    {
        _lastError = null;
        try
        {
            Directory.CreateDirectory(BaseDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
            return true;
        }
        catch (IOException ex)
        {
            _lastError = $"Failed to write settings file: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _lastError = $"Access denied to settings file: {ex.Message}";
            return false;
        }
    }
}
