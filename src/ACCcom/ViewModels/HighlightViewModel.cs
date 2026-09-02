using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

/// <summary>
/// Drives the highlight-rule editor window. Wraps <see cref="HighlightService"/>
/// so the UI can bind directly to <see cref="HighlightRule"/> instances, and
/// keeps the on-disk JSON file in sync on save. Holds a reference to the live
/// <see cref="DataFlowViewModel"/> so that when rules change we can recompute
/// the <c>HighlightColor</c> for entries that are already in the buffers.
/// </summary>
public class HighlightViewModel : ObservableObject
{
    private const string RulesFile = "highlights.json";

    private readonly HighlightService _service;
    private readonly Func<DataFlowViewModel> _getDataFlow;
    private readonly Action<string> _setStatus;

    public ObservableCollection<HighlightRule> Rules => _service.Rules;

    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand SaveRulesCommand { get; }
    public ICommand LoadRulesCommand { get; }
    public ICommand OpenEditDialogCommand { get; }

    public HighlightViewModel(
        HighlightService service,
        Func<DataFlowViewModel> getDataFlow,
        Action<string> setStatus)
    {
        _service = service;
        _getDataFlow = getDataFlow;
        _setStatus = setStatus;

        AddRuleCommand = new RelayCommand(_ => AddDefaultRule());
        DeleteRuleCommand = new RelayCommand(p => { if (p is HighlightRule r) DeleteRule(r); });
        SaveRulesCommand = new RelayCommand(_ => Save());
        LoadRulesCommand = new RelayCommand(_ => Load());
        OpenEditDialogCommand = new RelayCommand(p => { if (p is HighlightRule r) OpenEditDialog(r); });

        // Persist on every structural change so the user never loses work to a
        // crash — the rule list lives in %LOCALAPPDATA% anyway.
        _service.Rules.CollectionChanged += OnRulesChanged;
    }

    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Save();

    /// <summary>Returns the highlight color for a given entry, or null when
    /// no rule matches. Convenience wrapper around the service so the UI can
    /// call into the VM without knowing about <see cref="HighlightService"/>.</summary>
    public string? GetColor(LogEntry entry) => _service.GetHighlightColor(entry);

    /// <summary>Recompute HighlightColor for everything currently visible in
    /// the RX / TX buffers. Called when the user finishes editing a rule so
    /// the data panel reflects the new colors without losing scroll position.</summary>
    public void RefreshExisting()
    {
        var df = _getDataFlow();
        if (df == null) return;
        foreach (var entry in df.RxEntries) entry.HighlightColor = _service.GetHighlightColor(entry);
        foreach (var entry in df.TxEntries) entry.HighlightColor = _service.GetHighlightColor(entry);
    }

    private HighlightRule AddDefaultRule()
    {
        var rule = new HighlightRule
        {
            Name = $"Rule_{_service.Rules.Count + 1}",
            Pattern = "",
            Color = "#FF6B6B",
            MatchType = HighlightMatchType.Contains,
            Priority = 0,
            IsEnabled = true
        };
        _service.AddRule(rule);
        OpenEditDialog(rule);
        return rule;
    }

    private void DeleteRule(HighlightRule rule)
    {
        _service.RemoveRule(rule.Name);
        RefreshExisting();
    }

    private void OpenEditDialog(HighlightRule rule)
    {
        var dialog = new HighlightRuleDialog(rule);
        dialog.Owner = System.Windows.Application.Current?.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var updated = dialog.Rule;
            // HighlightService.AddRule replaces by name — works for both new
            // and edited rules.
            _service.AddRule(updated);
            RefreshExisting();
        }
        else
        {
            // User cancelled the very first rule they just added: roll back so
            // the list doesn't accumulate half-edited ghosts.
            if (!_service.Rules.Contains(rule))
                return;
            // Only delete if the rule wasn't actually edited (still has default placeholder name)
            if (rule.Name.StartsWith("Rule_") && string.IsNullOrEmpty(rule.Pattern))
                _service.RemoveRule(rule.Name);
        }
    }

    public void Save()
    {
        try
        {
            _service.Save();
            _setStatus(string.Format(LanguageManager.Instance["Status.HighlightsSaved"], _service.Rules.Count));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.SaveHighlightsFailed"], ex.Message));
        }
    }

    public void Load()
    {
        try
        {
            _service.Load();
            OnPropertyChanged(nameof(Rules));
        }
        catch (Exception ex)
        {
            _setStatus(string.Format(LanguageManager.Instance["Status.LoadHighlightsFailed"], ex.Message));
        }
    }
}