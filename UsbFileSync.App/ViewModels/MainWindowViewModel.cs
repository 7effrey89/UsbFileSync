using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using UsbFileSync.App.Commands;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromMilliseconds(250);

    private readonly SyncService _syncService;
    private readonly ISyncSettingsStore? _settingsStore;
    private CancellationTokenSource? _persistConfigurationCancellationTokenSource;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.OneWay;
    private bool _detectMoves = true;
    private bool _dryRun = true;
    private bool _isBusy;
    private bool _isLoadingSavedConfiguration;
    private double _progressValue;
    private string _statusMessage = "Configure the source and destination drives to begin.";

    public MainWindowViewModel()
        : this(new SyncService(), CreateDefaultSettingsStore())
    {
    }

    public MainWindowViewModel(SyncService syncService, ISyncSettingsStore? settingsStore = null)
    {
        _syncService = syncService;
        _settingsStore = settingsStore;
        AvailableModes = Enum.GetValues<SyncMode>();
        PlannedActions = new ObservableCollection<SyncAction>();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanExecuteSyncCommands);
        StartSyncCommand = new AsyncRelayCommand(StartSyncAsync, CanExecuteSyncCommands);
        LoadSavedConfiguration();
    }

    public IEnumerable<SyncMode> AvailableModes { get; }

    public ObservableCollection<SyncAction> PlannedActions { get; }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand StartSyncCommand { get; }

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public SyncMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public bool DetectMoves
    {
        get => _detectMoves;
        set
        {
            if (SetProperty(ref _detectMoves, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public bool DryRun
    {
        get => _dryRun;
        set
        {
            if (SetProperty(ref _dryRun, value))
            {
                HandleConfigurationChanged();
            }
        }
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

    private static ISyncSettingsStore CreateDefaultSettingsStore()
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbFileSync",
            "settings.json");

        return new JsonSyncSettingsStore(settingsPath);
    }

    private bool CanExecuteSyncCommands() => !IsBusy && IsConfigurationComplete();

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
        if (!TryValidateConfiguration())
        {
            return;
        }

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
        if (!TryValidateConfiguration())
        {
            return;
        }

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

    private void HandleConfigurationChanged()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        StartSyncCommand.RaiseCanExecuteChanged();

        if (_isLoadingSavedConfiguration)
        {
            return;
        }

        ScheduleConfigurationPersist();
    }

    private bool IsConfigurationComplete() =>
        !string.IsNullOrWhiteSpace(SourcePath) &&
        !string.IsNullOrWhiteSpace(DestinationPath) &&
        !string.Equals(SourcePath, DestinationPath, StringComparison.OrdinalIgnoreCase);

    private bool TryValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            StatusMessage = "Choose a primary drive path before running synchronization.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusMessage = "Choose a secondary drive path before running synchronization.";
            return false;
        }

        if (string.Equals(SourcePath, DestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Primary and secondary drive paths must be different.";
            return false;
        }

        return true;
    }

    private void LoadSavedConfiguration()
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            var savedConfiguration = _settingsStore.Load();
            if (savedConfiguration is null)
            {
                return;
            }

            _isLoadingSavedConfiguration = true;
            SourcePath = savedConfiguration.SourcePath;
            DestinationPath = savedConfiguration.DestinationPath;
            SelectedMode = savedConfiguration.Mode;
            DetectMoves = savedConfiguration.DetectMoves;
            DryRun = savedConfiguration.DryRun;
            StatusMessage = IsConfigurationComplete()
                ? "Restored the previous sync configuration."
                : "Configure the source and destination drives to begin.";
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Unable to read the saved sync configuration. {exception}");
            StatusMessage = "Couldn't read the saved sync configuration.";
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Access to the saved sync configuration was denied. {exception}");
            StatusMessage = "Couldn't access the saved sync configuration.";
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Unexpected error while loading the saved sync configuration. {exception}");
            StatusMessage = "Couldn't load the saved sync configuration.";
        }
        finally
        {
            _isLoadingSavedConfiguration = false;
            AnalyzeCommand.RaiseCanExecuteChanged();
            StartSyncCommand.RaiseCanExecuteChanged();
        }
    }

    private void ScheduleConfigurationPersist()
    {
        if (_settingsStore is null)
        {
            return;
        }

        _persistConfigurationCancellationTokenSource?.Cancel();
        var cancellationTokenSource = new CancellationTokenSource();
        _persistConfigurationCancellationTokenSource = cancellationTokenSource;
        _ = PersistConfigurationAsync(cancellationTokenSource);
    }

    private async Task PersistConfigurationAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await Task.Delay(SettingsSaveDelay, cancellationTokenSource.Token).ConfigureAwait(true);
            PersistConfigurationNow();
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Unable to save the sync configuration. {exception}");
            StatusMessage = "Couldn't save the sync configuration.";
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Access to the sync configuration file was denied. {exception}");
            StatusMessage = "Couldn't access the sync configuration file.";
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Unexpected error while saving the sync configuration. {exception}");
            StatusMessage = "Couldn't save the sync configuration.";
        }
        finally
        {
            if (ReferenceEquals(_persistConfigurationCancellationTokenSource, cancellationTokenSource))
            {
                _persistConfigurationCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private void PersistConfigurationNow()
    {
        if (_settingsStore is null)
        {
            return;
        }

        _settingsStore.Save(CreateConfiguration());
    }

    public void Dispose()
    {
        _persistConfigurationCancellationTokenSource?.Cancel();
        _persistConfigurationCancellationTokenSource?.Dispose();
        _persistConfigurationCancellationTokenSource = null;

        try
        {
            PersistConfigurationNow();
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Unable to save the sync configuration during shutdown. {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Access to the sync configuration file was denied during shutdown. {exception}");
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Unexpected error while saving the sync configuration during shutdown. {exception}");
        }

        GC.SuppressFinalize(this);
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
