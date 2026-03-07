using System.Collections.ObjectModel;
using UsbFileSync.App.Commands;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly SyncService _syncService;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.OneWay;
    private bool _detectMoves = true;
    private bool _dryRun = true;
    private bool _isBusy;
    private double _progressValue;
    private string _statusMessage = "Configure the source and destination drives to begin.";

    public MainWindowViewModel()
        : this(new SyncService())
    {
    }

    public MainWindowViewModel(SyncService syncService)
    {
        _syncService = syncService;
        AvailableModes = Enum.GetValues<SyncMode>();
        PlannedActions = new ObservableCollection<SyncAction>();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy);
        StartSyncCommand = new AsyncRelayCommand(StartSyncAsync, () => !IsBusy);
    }

    public IEnumerable<SyncMode> AvailableModes { get; }

    public ObservableCollection<SyncAction> PlannedActions { get; }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand StartSyncCommand { get; }

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set => SetProperty(ref _destinationPath, value);
    }

    public SyncMode SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public bool DetectMoves
    {
        get => _detectMoves;
        set => SetProperty(ref _detectMoves, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => SetProperty(ref _dryRun, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AnalyzeCommand.RaiseCanExecuteChanged();
                StartSyncCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private SyncConfiguration CreateConfiguration() => new()
    {
        SourcePath = SourcePath,
        DestinationPath = DestinationPath,
        Mode = SelectedMode,
        DetectMoves = DetectMoves,
        DryRun = DryRun,
    };

    private async Task AnalyzeAsync()
    {
        await RunBusyOperationAsync(async () =>
        {
            var actions = await _syncService.AnalyzeChangesAsync(CreateConfiguration()).ConfigureAwait(true);
            ReplaceActions(actions);
            StatusMessage = actions.Count == 0 ? "Folders are already synchronized." : $"Preview generated with {actions.Count} planned action(s).";
            ProgressValue = 0;
        }).ConfigureAwait(true);
    }

    private async Task StartSyncAsync()
    {
        await RunBusyOperationAsync(async () =>
        {
            var progress = new Progress<SyncProgress>(update =>
            {
                ProgressValue = update.TotalOperations == 0
                    ? 0
                    : Math.Round((double)update.CompletedOperations / update.TotalOperations * 100, 0);
                StatusMessage = $"Processing {update.CurrentItem} ({update.CompletedOperations}/{update.TotalOperations}).";
            });

            var result = await _syncService.ExecuteAsync(CreateConfiguration(), progress).ConfigureAwait(true);
            ReplaceActions(result.Actions);
            StatusMessage = result.IsDryRun
                ? $"Dry run complete with {result.Actions.Count} planned action(s)."
                : $"Synchronization complete. Applied {result.AppliedOperations} action(s).";

            if (!result.IsDryRun)
            {
                ProgressValue = 100;
            }
        }).ConfigureAwait(true);
    }

    private async Task RunBusyOperationAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Working...";
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceActions(IEnumerable<SyncAction> actions)
    {
        PlannedActions.Clear();
        foreach (var action in actions)
        {
            PlannedActions.Add(action);
        }
    }
}
