using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class TriggerViewModel : ObservableObject
{
    private readonly ISerialService _serial;
    private readonly TriggerService _triggerService;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;

    public ObservableCollection<TriggerRule> TriggerRules { get; } = new();

    public ICommand SaveTriggersCommand { get; }
    public ICommand LoadTriggersCommand { get; }
    public ICommand AddTriggerCommand { get; }
    public ICommand DeleteTriggerCommand { get; }

    public TriggerViewModel(
        ISerialService serial,
        TriggerService triggerService,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _serial = serial;
        _triggerService = triggerService;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        SaveTriggersCommand = new RelayCommand(_ => SaveTriggers());
        LoadTriggersCommand = new RelayCommand(_ => LoadTriggers());
        AddTriggerCommand = new RelayCommand(_ => AddTrigger());
        DeleteTriggerCommand = new RelayCommand(p => { if (p is TriggerRule r) DeleteTrigger(r); });
    }

    public void LoadTriggers()
    {
        try
        {
            var rules = TriggerService.LoadRules("triggers.json");
            foreach (var r in rules)
            {
                _triggerService.AddRule(r);
                TriggerRules.Add(r);
            }
        }
        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.LoadTriggersFailed"], ex.Message)); }
    }

    private void SaveTriggers()
    {
        try
        {
            TriggerService.SaveRules(TriggerRules, "triggers.json");
            _setStatus(string.Format(LanguageManager.Instance["Status.TriggersSaved"], TriggerRules.Count));
        }
        catch (Exception ex) { _setStatus($"Failed to save triggers: {ex.Message}"); }
    }

    private void AddTrigger()
    {
        var rule = new TriggerRule
        {
            Name = $"Rule_{TriggerRules.Count + 1}",
            Pattern = "",
            MatchMode = "contains",
            Action = TriggerAction.None
        };
        _triggerService.AddRule(rule);
        TriggerRules.Add(rule);
    }

    private void DeleteTrigger(TriggerRule rule)
    {
        _triggerService.RemoveRule(rule.Name);
        TriggerRules.Remove(rule);
    }

    public void OnTriggerFired(TriggerRule rule, LogEntry entry)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            switch (rule.Action)
            {
                case TriggerAction.SendCommand:
                    if (!string.IsNullOrEmpty(rule.ActionParameter))
                    {
                        _serial.Send(rule.ActionParameter, false);
                        var df = _getDataFlow();
                        df.TxCount++;
                        df.RecordTxBytes(System.Text.Encoding.UTF8.GetByteCount(rule.ActionParameter));
                    }
                    break;
                case TriggerAction.SaveToFile:
                    if (!string.IsNullOrEmpty(rule.ActionParameter))
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(rule.ActionParameter);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            var line = $"[{entry.Timestamp:HH:mm:ss.fff}] {entry.Direction} {entry.Text}";
                            File.AppendAllText(rule.ActionParameter, line + Environment.NewLine);
                        }
                        catch (Exception ex) { _setStatus(string.Format(LanguageManager.Instance["Status.SaveTriggersFailed"], ex.Message)); }
                    }
                    break;
                case TriggerAction.LogMessage:
                    _setStatus(string.Format(LanguageManager.Instance["Status.TriggerFired"], rule.Name, rule.ActionParameter ?? rule.Pattern));
                    break;
                case TriggerAction.PlaySound:
                    System.Media.SystemSounds.Asterisk.Play();
                    break;
            }
        });
    }
}
