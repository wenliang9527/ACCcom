using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class PresetViewModel : ObservableObject
{
    private readonly PresetManager _presetManager;
    private readonly Func<ConnectionViewModel> _getConnection;
    private readonly Action<string> _setStatus;

    public ObservableCollection<SerialPreset> Presets { get; } = new();

    private SerialPreset? _selectedPreset;
    public SerialPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetField(ref _selectedPreset, value) && value != null)
                ApplyPreset(value);
        }
    }

    public ICommand SavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }

    public PresetViewModel(
        PresetManager presetManager,
        Func<ConnectionViewModel> getConnection,
        Action<string> setStatus)
    {
        _presetManager = presetManager;
        _getConnection = getConnection;
        _setStatus = setStatus;

        SavePresetCommand = new RelayCommand(_ => SavePreset());
        DeletePresetCommand = new RelayCommand(p => { if (p is SerialPreset s) DeletePreset(s); });
    }

    public async Task LoadPresetsAsync()
    {
        try
        {
            var items = await _presetManager.LoadAsync();
            foreach (var p in items) Presets.Add(p);
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.LoadPresetsFailed"], ex.Message)); }
    }

    private void SavePresetsToFile()
    {
        try { _presetManager.Save(Presets); }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.SavePresetsFailed"], ex.Message)); }
    }

    private void ApplyPreset(SerialPreset p)
    {
        var conn = _getConnection();
        conn.SelectedPort = p.Port;
        conn.SelectedBaudRate = p.BaudRate;
        conn.SelectedDataBits = p.DataBits;
        conn.SelectedStopBits = p.StopBits;
        conn.SelectedParity = p.Parity;
        conn.DtrEnable = p.Dtr;
        conn.RtsEnable = p.Rts;
        _setStatus(string.Format(LanguageManager.Instance["Status.PresetLoaded"], p.Name));
    }

    private void SavePreset()
    {
        var conn = _getConnection();
        if (string.IsNullOrEmpty(conn.SelectedPort)) { _setStatus(LanguageManager.Instance["Status.PleaseSelectPortFirst"]); return; }
        var preset = new SerialPreset
        {
            Name = $"{conn.SelectedPort}@{conn.SelectedBaudRate}",
            Port = conn.SelectedPort,
            BaudRate = conn.SelectedBaudRate,
            DataBits = conn.SelectedDataBits,
            StopBits = conn.SelectedStopBits,
            Parity = conn.SelectedParity,
            Dtr = conn.DtrEnable,
            Rts = conn.RtsEnable
        };
        Presets.Add(preset);
        SavePresetsToFile();
        _setStatus(string.Format(LanguageManager.Instance["Status.PresetSaved"], preset.Name));
    }

    private void DeletePreset(SerialPreset preset)
    {
        Presets.Remove(preset);
        SavePresetsToFile();
        _setStatus(string.Format(LanguageManager.Instance["Status.PresetDeleted"], preset.Name));
    }
}
