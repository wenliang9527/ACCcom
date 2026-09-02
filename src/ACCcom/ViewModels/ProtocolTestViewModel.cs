using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

/// <summary>
/// Editor + runner for protocol regression tests. Wraps <see cref="ProtocolTestRunner"/>
/// so the UI can author a script (steps with send/expect assertions), run it against
/// the live serial connection, and see per-step pass/fail results in real time.
/// </summary>
public class ProtocolTestViewModel : ObservableObject, IDisposable
{
    private static readonly string ScriptsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACCcom", "scripts");

    private readonly ProtocolTestRunner _runner;
    private readonly ISerialService _serial;
    private readonly Func<bool> _getIsOpen;
    private readonly Action<string> _setStatus;
    private readonly ConcurrentQueue<LogEntry> _rxQueue = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    private string _scriptName = "Untitled";
    private string _description = "";
    private int _repeatCount = 1;
    private int _repeatDelayMs;

    public string ScriptName { get => _scriptName; set => SetField(ref _scriptName, value ?? ""); }
    public string Description { get => _description; set => SetField(ref _description, value ?? ""); }
    public int RepeatCount { get => _repeatCount; set => SetField(ref _repeatCount, value); }
    public int RepeatDelayMs { get => _repeatDelayMs; set => SetField(ref _repeatDelayMs, value); }

    public ObservableCollection<TestStep> Steps { get; } = new();
    public ObservableCollection<TestStepResult> Results { get; } = new();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public int PassedCount => Results.Count(r => r.Passed);
    public int FailedCount => Results.Count(r => !r.Passed);

    public ICommand AddStepCommand { get; }
    public ICommand RemoveStepCommand { get; }
    public ICommand RunTestsCommand { get; }
    public ICommand StopTestsCommand { get; }
    public ICommand NewScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand LoadScriptCommand { get; }

    public ProtocolTestViewModel(
        ProtocolTestRunner runner,
        ISerialService serial,
        Func<bool> getIsOpen,
        Action<string> setStatus)
    {
        _runner = runner;
        _serial = serial;
        _getIsOpen = getIsOpen;
        _setStatus = setStatus;

        AddStepCommand = new RelayCommand(_ => AddStep());
        RemoveStepCommand = new RelayCommand(p => { if (p is TestStep s) RemoveStep(s); });
        RunTestsCommand = new RelayCommand(_ => _ = RunTestsAsync(), _ => !IsRunning);
        StopTestsCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
        NewScriptCommand = new RelayCommand(_ => NewScript());
        SaveScriptCommand = new RelayCommand(_ => SaveScript());
        LoadScriptCommand = new RelayCommand(_ => LoadScript());
    }

    private void AddStep()
    {
        var step = new TestStep
        {
            Name = $"Step {Steps.Count + 1}",
            Command = "",
            ExpectedPattern = null
        };
        Steps.Add(step);
    }

    private void RemoveStep(TestStep step) => Steps.Remove(step);

    private void NewScript()
    {
        Steps.Clear();
        Results.Clear();
        ScriptName = "Untitled";
        Description = "";
        RepeatCount = 1;
        RepeatDelayMs = 0;
        _setStatus(LanguageManager.Instance["Status.ProtocolTestNew"]);
    }

    private void SaveScript()
    {
        try
        {
            Directory.CreateDirectory(ScriptsDir);
            var path = Path.Combine(ScriptsDir, SafeFileName(ScriptName) + ".json");
            var script = BuildScript();
            ProtocolTestRunner.SaveScript(script, path);
            _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestSaved"], Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestSaveFailed"], ex.Message));
        }
    }

    private void LoadScript()
    {
        try
        {
            Directory.CreateDirectory(ScriptsDir);
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Test scripts (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = ScriptsDir
            };
            if (dialog.ShowDialog() != true) return;

            var script = ProtocolTestRunner.LoadScript(dialog.FileName);
            ApplyScript(script);
            _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestLoaded"], Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestLoadFailed"], ex.Message));
        }
    }

    private TestScript BuildScript() => new()
    {
        Name = ScriptName,
        Description = Description,
        RepeatCount = RepeatCount,
        RepeatDelayMs = RepeatDelayMs,
        Steps = Steps.ToList()
    };

    private void ApplyScript(TestScript script)
    {
        Steps.Clear();
        foreach (var s in script.Steps) Steps.Add(s);
        ScriptName = script.Name;
        Description = script.Description;
        RepeatCount = script.RepeatCount;
        RepeatDelayMs = script.RepeatDelayMs;
        Results.Clear();
    }

    /// <summary>Feed incoming RX entries here. Called from MainViewModel's
    /// OnEntryProcessed hook; the queue is a cheap no-op when not testing.</summary>
    public void OnRxEntry(LogEntry entry)
    {
        if (_cts == null) return;
        _rxQueue.Enqueue(entry);
    }

    private async Task RunTestsAsync()
    {
        if (!_getIsOpen())
        {
            _setStatus(LanguageManager.Instance["Status.PleaseSelectPortFirst"]);
            return;
        }

        Results.Clear();
        _rxQueue.Clear();
        _cts = new CancellationTokenSource();

        var script = BuildScript();
        IsRunning = true;
        _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestRunning"], script.Name));

        try
        {
            var report = await _runner.RunAsync(script, SendCallback, WaitForResponseAsync, _cts.Token);

            foreach (var result in report.Results)
            {
                Results.Add(result);
            }
            OnPropertyChanged(nameof(PassedCount));
            OnPropertyChanged(nameof(FailedCount));
            _setStatus(string.Format(
                report.AllPassed
                    ? LanguageManager.Instance["Status.ProtocolTestCompleted"]
                    : LanguageManager.Instance["Status.ProtocolTestFailed"],
                report.Passed, report.Failed, report.Total));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.ProtocolTestError"], ex.Message));
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void SendCallback(string command, bool isHex)
    {
        _serial.Send(command, isHex);
    }

    private async Task<string?> WaitForResponseAsync(
        string pattern, string matchMode, bool matchHex, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            while (_rxQueue.TryDequeue(out var entry))
            {
                if (ProtocolTestRunner.TryMatchEntry(entry, pattern, matchMode, matchHex, out var matched))
                    return matched;
            }

            await Task.Delay(30, ct).ConfigureAwait(false);
        }
        return null;
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "script" : name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _runner.Dispose();
    }
}