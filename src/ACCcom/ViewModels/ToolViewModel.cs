using System.Collections.ObjectModel;
using System.Windows.Input;
using ACCcom.Core.Models;
using ACCcom.Core.Services;

namespace ACCcom.ViewModels;

public class ToolViewModel : ObservableObject, IDisposable
{
    private readonly MacroManager _macroManager;
    private readonly Action _openPlotWindow;
    private readonly Action _openStatsWindow;

    public ShortcutViewModel Shortcuts { get; }
    public PresetViewModel PresetsVm { get; }
    public LoopSendViewModel LoopSend { get; }
    public MultiPortViewModel MultiPort { get; }
    public TriggerViewModel Triggers { get; }
    public BookmarkViewModel BookmarksVm { get; }
    public MacroViewModel MacrosVm { get; }
    public ReplayViewModel Replay { get; }

    public ObservableCollection<ShortcutItem> ShortcutCommands => Shortcuts.ShortcutCommands;
    public ObservableCollection<SerialPreset> Presets => PresetsVm.Presets;
    public SerialPreset? SelectedPreset { get => PresetsVm.SelectedPreset; set => PresetsVm.SelectedPreset = value; }
    public ObservableCollection<MacroTemplate> Macros => MacrosVm.Macros;
    public bool IsMacroRunning { get => MacrosVm.IsMacroRunning; set => MacrosVm.IsMacroRunning = value; }
    public string MacroStatus { get => MacrosVm.MacroStatus; set => MacrosVm.MacroStatus = value; }
    public ObservableCollection<PortItemViewModel> ConnectedPorts => MultiPort.ConnectedPorts;
    public string NewPortTag { get => MultiPort.NewPortTag; set => MultiPort.NewPortTag = value; }
    public string NewPortName { get => MultiPort.NewPortName; set => MultiPort.NewPortName = value; }
    public int NewPortBaud { get => MultiPort.NewPortBaud; set => MultiPort.NewPortBaud = value; }
    public ObservableCollection<TriggerRule> TriggerRules => Triggers.TriggerRules;
    public ObservableCollection<BookmarkItem> Bookmarks => BookmarksVm.Bookmarks;
    public int CurrentBookmarkIndex { get => BookmarksVm.CurrentBookmarkIndex; set => BookmarksVm.CurrentBookmarkIndex = value; }
    public bool IsLoopSend { get => LoopSend.IsLoopSend; set => LoopSend.IsLoopSend = value; }
    public int LoopInterval { get => LoopSend.LoopInterval; set => LoopSend.LoopInterval = value; }
    public bool IsLooping { get => LoopSend.IsLooping; set => LoopSend.IsLooping = value; }

    public ICommand SendShortcutCommand => Shortcuts.SendShortcutCommand;
    public ICommand AddShortcutCommand => Shortcuts.AddShortcutCommand;
    public ICommand DeleteShortcutCommand => Shortcuts.DeleteShortcutCommand;
    public ICommand SavePresetCommand => PresetsVm.SavePresetCommand;
    public ICommand DeletePresetCommand => PresetsVm.DeletePresetCommand;
    public ICommand RunMacroCommand => MacrosVm.RunMacroCommand;
    public ICommand StopMacroCommand => MacrosVm.StopMacroCommand;
    public ICommand SaveMacroCommand => MacrosVm.SaveMacroCommand;
    public ICommand LoadMacroCommand => MacrosVm.LoadMacroCommand;
    public ICommand OpenMultiPortCommand => MultiPort.OpenMultiPortCommand;
    public ICommand CloseMultiPortCommand => MultiPort.CloseMultiPortCommand;
    public ICommand CloseAllPortsCommand => MultiPort.CloseAllPortsCommand;
    public ICommand SaveTriggersCommand => Triggers.SaveTriggersCommand;
    public ICommand LoadTriggersCommand => Triggers.LoadTriggersCommand;
    public ICommand AddTriggerCommand => Triggers.AddTriggerCommand;
    public ICommand DeleteTriggerCommand => Triggers.DeleteTriggerCommand;
    public ICommand AddBookmarkCommand => BookmarksVm.AddBookmarkCommand;
    public ICommand RemoveBookmarkCommand => BookmarksVm.RemoveBookmarkCommand;
    public ICommand NextBookmarkCommand => BookmarksVm.NextBookmarkCommand;
    public ICommand PrevBookmarkCommand => BookmarksVm.PrevBookmarkCommand;
    public ICommand ReplayFileCommand => Replay.ReplayFileCommand;
    public ICommand StopLoopCommand => LoopSend.StopLoopCommand;
    public ICommand OpenPlotCommand { get; }
    public ICommand OpenStatsCommand { get; }

    private StatsViewModel? _statsViewModel;
    public StatsViewModel? StatsViewModel => _statsViewModel;

    public ToolViewModel(
        ISerialService serial,
        ShortcutManager shortcutManager,
        PresetManager presetManager,
        MacroManager macroManager,
        BookmarkManager bookmarkManager,
        MultiPortService multiPort,
        TriggerService triggerService,
        SessionRecorder sessionRecorder,
        Action<string> setStatus,
        Func<bool> getIsOpen,
        Func<DataFlowViewModel> getDataFlow,
        Func<ConnectionViewModel> getConnection,
        Action openPlotWindow,
        Action openStatsWindow)
    {
        _macroManager = macroManager;
        _openPlotWindow = openPlotWindow;
        _openStatsWindow = openStatsWindow;

        Shortcuts = new ShortcutViewModel(serial, shortcutManager, getDataFlow, setStatus);
        PresetsVm = new PresetViewModel(presetManager, getConnection, setStatus);
        LoopSend = new LoopSendViewModel(serial, getIsOpen, getDataFlow, setStatus);
        MultiPort = new MultiPortViewModel(multiPort, setStatus);
        Triggers = new TriggerViewModel(serial, triggerService, getDataFlow, setStatus);
        BookmarksVm = new BookmarkViewModel(bookmarkManager, getDataFlow, setStatus);
        MacrosVm = new MacroViewModel(serial, macroManager, getDataFlow, getIsOpen, setStatus);
        Replay = new ReplayViewModel(sessionRecorder, getDataFlow, setStatus);

        OpenPlotCommand = new RelayCommand(_ => _openPlotWindow());
        OpenStatsCommand = new RelayCommand(_ => OpenStatsWindow());

        ForwardPropertyChanged(Shortcuts);
        ForwardPropertyChanged(PresetsVm);
        ForwardPropertyChanged(LoopSend);
        ForwardPropertyChanged(MultiPort);
        ForwardPropertyChanged(Triggers);
        ForwardPropertyChanged(BookmarksVm);
        ForwardPropertyChanged(MacrosVm);
        ForwardPropertyChanged(Replay);
    }

    private void ForwardPropertyChanged(ObservableObject vm)
    {
        vm.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    public async Task LoadShortcutsAsync() => await Shortcuts.LoadShortcutsAsync();

    public async Task LoadPresetsAsync() => await PresetsVm.LoadPresetsAsync();

    public async Task LoadMacrosAsync() => await MacrosVm.LoadMacrosAsync();

    public void LoadTriggers() => Triggers.LoadTriggers();

    public void OnTriggerFired(TriggerRule rule, LogEntry entry) => Triggers.OnTriggerFired(rule, entry);

    private void OpenStatsWindow()
    {
        _statsViewModel = new StatsViewModel();
        _openStatsWindow();
    }

    public void Dispose()
    {
        Replay.CloseWindow();
        _macroManager.Dispose();
        LoopSend.StopLoop();
    }
}
