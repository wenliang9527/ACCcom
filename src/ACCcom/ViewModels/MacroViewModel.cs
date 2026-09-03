using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class MacroViewModel : ObservableObject
{
    private readonly ISerialService _serial;
    private readonly MacroManager _macroManager;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Func<bool> _getIsOpen;
    private readonly Action<string> _setStatus;

    public ObservableCollection<MacroTemplate> Macros { get; } = new();

    private MacroTemplate? _selectedMacro;
    public MacroTemplate? SelectedMacro
    {
        get => _selectedMacro;
        set => SetField(ref _selectedMacro, value);
    }

    private bool _isMacroRunning;
    public bool IsMacroRunning { get => _isMacroRunning; set => SetField(ref _isMacroRunning, value); }

    private string _macroStatus = "";
    public string MacroStatus { get => _macroStatus; set => SetField(ref _macroStatus, value); }

    public ICommand RunMacroCommand { get; }
    public ICommand StopMacroCommand { get; }
    public ICommand SaveMacroCommand { get; }
    public ICommand LoadMacroCommand { get; }
    public ICommand AddMacroCommand { get; }
    public ICommand DeleteMacroCommand { get; }
    public ICommand AddStepCommand { get; }
    public ICommand RemoveStepCommand { get; }

    public MacroViewModel(
        ISerialService serial,
        MacroManager macroManager,
        Func<DataFlowViewModel> getDataFlow,
        Func<bool> getIsOpen,
        Action<string> setStatus)
    {
        _serial = serial;
        _macroManager = macroManager;
        _getDataFlow = getDataFlow;
        _getIsOpen = getIsOpen;
        _setStatus = setStatus;

        RunMacroCommand = new RelayCommand(_ => _ = RunMacroAsync(), _ => !IsMacroRunning && _getIsOpen());
        StopMacroCommand = new RelayCommand(_ => StopMacro(), _ => IsMacroRunning);
        SaveMacroCommand = new RelayCommand(_ => SaveMacro());
        LoadMacroCommand = new RelayCommand(_ => LoadMacro());
        AddMacroCommand = new RelayCommand(_ => AddMacro());
        DeleteMacroCommand = new RelayCommand(_ => DeleteMacro(), _ => SelectedMacro != null);
        AddStepCommand = new RelayCommand(_ => AddStep(), _ => SelectedMacro != null);
        RemoveStepCommand = new RelayCommand(_ => RemoveStep(), _ => SelectedMacro?.Steps.Count > 0);
    }

    public async Task LoadMacrosAsync()
    {
        try
        {
            var items = await _macroManager.LoadAsync();
            foreach (var m in items) Macros.Add(m);
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.LoadMacrosFailed"], ex.Message)); }
    }

    private void SaveMacro()
    {
        try
        {
            _macroManager.Save(Macros);
            _setStatus(string.Format(LanguageManager.Instance["Status.MacrosSaved"], Macros.Count));
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.MacrosSaveFailed"], ex.Message)); }
    }

    private void LoadMacro()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Macro files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var items = _macroManager.LoadFromFile(dialog.FileName);
            foreach (var m in items) Macros.Add(m);
            _setStatus(string.Format(LanguageManager.Instance["Status.MacrosImported"], items.Length));
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.ImportMacrosFailed"], ex.Message)); }
    }

    private async Task RunMacroAsync()
    {
        var macro = SelectedMacro ?? Macros.FirstOrDefault();
        if (macro == null) { _setStatus(LanguageManager.Instance["Status.NoMacros"]); return; }
        IsMacroRunning = true;
        MacroStatus = string.Format(LanguageManager.Instance["Status.MacroRunning"], macro.Name);

        try
        {
            var df = _getDataFlow();
            var completed = await _macroManager.RunAsync(
                macro,
                send: (cmd, isHex) => _serial.Send(cmd, isHex),
                expandVariables: df.ExpandVariables,
                updateStatus: status => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => MacroStatus = status));

            _setStatus(completed ? LanguageManager.Instance["Status.MacroCompleted"] : LanguageManager.Instance["Status.MacroStopped"]);
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.MacroError"], ex.Message)); }
        finally
        {
            IsMacroRunning = false;
            MacroStatus = "";
        }
    }

    private void StopMacro() => _macroManager.Stop();

    private void AddMacro()
    {
        var name = NextMacroName();
        var macro = new MacroTemplate
        {
            Name = name,
            Description = "",
            RepeatCount = 1,
            Steps = new List<MacroStep> { new() { Command = "", DelayMs = 100 } }
        };
        Macros.Add(macro);
        SelectedMacro = macro;
        _setStatus(string.Format(LanguageManager.Instance["Status.MacroCreated"], name));
    }

    private string NextMacroName() => MacroNaming.NextName(Macros.Select(m => m.Name));

    private void DeleteMacro()
    {
        if (SelectedMacro == null) return;
        var name = SelectedMacro.Name;
        Macros.Remove(SelectedMacro);
        SelectedMacro = null;
        try { _macroManager.Save(Macros); }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.MacrosSaveFailed"], ex.Message)); return; }
        _setStatus(string.Format(LanguageManager.Instance["Status.MacroDeleted"], name));
    }

    private void AddStep()
    {
        if (SelectedMacro == null) return;
        SelectedMacro.Steps.Add(new MacroStep { Command = "", DelayMs = 100 });
    }

    private void RemoveStep()
    {
        if (SelectedMacro?.Steps.Count > 0)
            SelectedMacro.Steps.RemoveAt(SelectedMacro.Steps.Count - 1);
    }
}
