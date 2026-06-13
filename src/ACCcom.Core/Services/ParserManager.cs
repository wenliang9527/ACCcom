using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserManager : IDisposable
{
    public const string NoParserName = "(None)";

    private readonly string _parserDir;
    private readonly ParserEngine _engine;
    private FileSystemWatcher? _watcher;
    private string? _activeParserPath;
    private System.Threading.Timer? _debounceTimer;
    private readonly object _debounceLock = new();
    private bool _disposed;

    public ObservableCollection<string> AvailableParsers { get; } = new();
    public string? ActiveParserName { get; private set; }
    public string? LastError => _engine.LastError;
    public ParserEngine Engine => _engine;
    public bool HotReloadEnabled { get; set; } = true;

    /// <summary>
    /// Raised after the active parser is reloaded due to a file change.
    /// The string parameter is the parser name that was reloaded.
    /// </summary>
    public event Action<string>? OnParserReloaded;

    /// <summary>
    /// Raised when an error occurs during hot-reload or other operations.
    /// UI can subscribe to display error messages to the user.
    /// </summary>
    public event Action<string, Exception>? ErrorOccurred;

    public ParserManager(string? parserDir = null, Action<Action>? dispatch = null, int parserCacheSize = 10)
    {
        _engine = new ParserEngine(parserCacheSize);
        _dispatch = dispatch;
        _parserDir = parserDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parsers");
        Directory.CreateDirectory(_parserDir);
        SetupWatcher();
        Refresh();
    }

    private readonly Action<Action>? _dispatch;

    private void Dispatch(Action action)
    {
        if (_dispatch != null)
            _dispatch(action);
        else
            action();
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(_parserDir, "*.csx")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };
        _watcher.Created += OnWatcherEvent;
        _watcher.Deleted += OnWatcherEvent;
        _watcher.Changed += OnWatcherEvent;
        _watcher.Renamed += OnWatcherRenamed;
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        if (!HotReloadEnabled) return;
        DebounceReload();
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        if (!HotReloadEnabled) return;
        DebounceReload();
    }

    private void DebounceReload()
    {
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                Dispatch(() =>
                {
                    try
                    {
                        Refresh();
                        if (ActiveParserName != null)
                        {
                            var path = Path.Combine(_parserDir, ActiveParserName + ".csx");
                            if (File.Exists(path))
                            {
                                var code = File.ReadAllText(path);
                                if (_engine.Load(code))
                                    OnParserReloaded?.Invoke(ActiveParserName);
                            }
                            else
                            {
                                Activate(null);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ParserManager] Hot-reload failed: {ex.Message}");
                        ErrorOccurred?.Invoke("Hot-reload failed", ex);
                    }
                });
            }, null, 500, System.Threading.Timeout.Infinite);
        }
    }

    public void Refresh()
    {
        var files = Directory.GetFiles(_parserDir, "*.csx")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x);

        AvailableParsers.Clear();
        AvailableParsers.Add(NoParserName);
        foreach (var f in files)
            AvailableParsers.Add(f!);

        if (ActiveParserName != null && !AvailableParsers.Contains(ActiveParserName))
            Activate(null);
    }

    public bool Activate(string? parserName)
    {
        parserName = parserName?.Trim();
        if (string.IsNullOrEmpty(parserName) || parserName == NoParserName)
        {
            ActiveParserName = null;
            _activeParserPath = null;
            _engine.Clear();
            return true;
        }

        var path = Path.Combine(_parserDir, parserName + ".csx");
        if (!File.Exists(path)) return false;

        var code = File.ReadAllText(path);
        if (!_engine.Load(code)) return false;

        ActiveParserName = parserName;
        _activeParserPath = path;
        return true;
    }

    public string GetParserDir() => _parserDir;

    /// <summary>
    /// 从 ProtocolSchema 生成并保存解析器
    /// </summary>
    public (bool success, string? error) GenerateParser(ProtocolSchema schema)
    {
        var generator = new ParserGenerator();
        var (valid, errors) = generator.Validate(schema);
        if (!valid)
            return (false, string.Join("\n", errors));

        try
        {
            var code = generator.Generate(schema);
            var path = Path.Combine(_parserDir, schema.Name + ".csx");
            File.WriteAllText(path, code);
            Refresh();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// 从 JSON 生成并保存
    /// </summary>
    public (bool success, string? error) GenerateParserFromJson(string json)
    {
        var generator = new ParserGenerator();
        var schema = generator.ParseJson(json);
        if (schema == null)
            return (false, "Invalid JSON schema");

        return GenerateParser(schema);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_debounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        _watcher?.Dispose();
    }
}
