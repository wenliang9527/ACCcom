using System.Collections.ObjectModel;
using System.IO;
using ACCcom.Core.Models;

namespace ACCcom.Core.Services;

public class ParserManager : IDisposable
{
    private readonly string _parserDir;
    private readonly ParserEngine _engine = new();
    private FileSystemWatcher? _watcher;
    private string? _activeParserPath;

    public ObservableCollection<string> AvailableParsers { get; } = new();
    public string? ActiveParserName { get; private set; }
    public string? LastError => _engine.LastError;
    public ParserEngine Engine => _engine;

    public ParserManager(string? parserDir = null, Action<Action>? dispatch = null)
    {
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
        _watcher.Created += (_, _) => Dispatch(Refresh);
        _watcher.Deleted += (_, _) => Dispatch(Refresh);
        _watcher.Changed += (_, _) =>
        {
            try
            {
                if (_activeParserPath != null && File.Exists(_activeParserPath))
                {
                    var code = File.ReadAllText(_activeParserPath);
                    Dispatch(() => _engine.Load(code));
                }
            }
            catch { }
        };
    }

    public void Refresh()
    {
        var files = Directory.GetFiles(_parserDir, "*.csx")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(x => x);

        AvailableParsers.Clear();
        AvailableParsers.Add("(无)");
        foreach (var f in files)
            AvailableParsers.Add(f!);

        if (ActiveParserName != null && !AvailableParsers.Contains(ActiveParserName))
            Activate(null);
    }

    public bool Activate(string? parserName)
    {
        if (string.IsNullOrEmpty(parserName) || parserName == "(无)")
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

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
