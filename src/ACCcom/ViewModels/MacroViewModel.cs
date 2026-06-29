using System.Collections.ObjectModel;
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

    private bool _isMacroRunning;
    public bool IsMacroRunning { get => _isMacroRunning; set => SetField(ref _isMacroRunning, value); }

    private string _macroStatus = "";
    public string MacroStatus { get => _macroStatus; set => SetField(ref _macroStatus, value); }

    public ICommand RunMacroCommand { get; }
    public ICommand StopMacroCommand { get; }
    public ICommand SaveMacroCommand { get; }
    public ICommand LoadMacroCommand { get; }

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
        if (Macros.Count == 0) { _setStatus(LanguageManager.Instance["Status.NoMacros"]); return; }
        var macro = Macros[0];
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
}
