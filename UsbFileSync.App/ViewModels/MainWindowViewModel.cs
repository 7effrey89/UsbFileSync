using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using UsbFileSync.App.Commands;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromMilliseconds(250);

    private readonly SyncService _syncService;
    private readonly ISyncSettingsStore? _settingsStore;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IDriveDisplayNameService _driveDisplayNameService;
    private readonly Dictionary<string, SyncPreviewRowViewModel> _previewRowsByRelativePath = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _persistConfigurationCancellationTokenSource;
    private CancellationTokenSource? _syncCancellationTokenSource;
    private IReadOnlyList<SyncAction> _queuedActions = [];
    private int _completedQueuedActions;
    private string _currentTransferItem = "No active transfer.";
    private string _currentTransferDetails = "Queue is idle.";
    private double _currentTransferProgressValue;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.OneWay;
    private int _parallelCopyCount = 1;
    private bool _detectMoves = true;
    private bool _dryRun = true;
    private bool _verifyChecksums;
    private bool _isBusy;
    private bool _isSyncRunning;
    private bool _isLoadingSavedConfiguration;
    private double _progressValue;
    private string _statusMessage = "Configure the source and destination drives to begin.";
    private bool _isStatusSuccess;
    private bool _isSourcePathFocused;
    private bool _isDestinationPathFocused;
    private ActivityLogFilter _selectedActivityLogFilter = ActivityLogFilter.All;

    public MainWindowViewModel()
        : this(new SyncService(), CreateDefaultSettingsStore(), new WindowsFolderPickerService(), new WindowsFileLauncherService(), new WindowsDriveDisplayNameService())
    {
    }

    public MainWindowViewModel(
        SyncService syncService,
        ISyncSettingsStore? settingsStore = null,
        IFolderPickerService? folderPickerService = null,
        IFileLauncherService? fileLauncherService = null,
        IDriveDisplayNameService? driveDisplayNameService = null)
    {
        _syncService = syncService;
        _settingsStore = settingsStore;
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _fileLauncherService = fileLauncherService ?? new WindowsFileLauncherService();
        _driveDisplayNameService = driveDisplayNameService ?? new WindowsDriveDisplayNameService();
        AvailableModes = Enum.GetValues<SyncMode>();
        PlannedActions = new ObservableCollection<SyncAction>();
        NewFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        ChangedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        DeletedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        UnchangedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        AllFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        ActivityLog = new ObservableCollection<SyncLogEntryViewModel>();
        ActivityLogView = CollectionViewSource.GetDefaultView(ActivityLog);
        ActivityLogView.Filter = ShouldIncludeActivityLogEntry;
        RemainingQueue = new ObservableCollection<QueueActionViewModel>();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanExecuteSyncCommands);
        ToggleSyncCommand = new RelayCommand(ToggleSync, CanExecuteToggleSyncCommand);
        BrowseSourcePathCommand = new RelayCommand(BrowseSourcePath);
        BrowseDestinationPathCommand = new RelayCommand(BrowseDestinationPath);
        OpenPreviewItemCommand = new ParameterizedRelayCommand(OpenPreviewItem, CanOpenPreviewItem);
        OpenPreviewContainingFolderCommand = new ParameterizedRelayCommand(OpenPreviewContainingFolder, CanOpenPreviewItem);
        OpenPreviewFileCommand = new ParameterizedRelayCommand(OpenPreviewFile, CanOpenPreviewFile);
        OpenDestinationPreviewItemCommand = new ParameterizedRelayCommand(OpenDestinationPreviewItem, CanOpenDestinationPreviewItem);
        OpenDestinationPreviewContainingFolderCommand = new ParameterizedRelayCommand(OpenDestinationPreviewContainingFolder, CanOpenDestinationPreviewItem);
        OpenDestinationPreviewFileCommand = new ParameterizedRelayCommand(OpenDestinationPreviewFile, CanOpenDestinationPreviewFile);
        LoadSavedConfiguration();
        AddLog("Info", "Application ready.");
    }

    public IEnumerable<SyncMode> AvailableModes { get; }

    public ObservableCollection<SyncAction> PlannedActions { get; }

    public ObservableCollection<SyncPreviewRowViewModel> NewFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> ChangedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> DeletedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> UnchangedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> AllFiles { get; }

    public ObservableCollection<SyncLogEntryViewModel> ActivityLog { get; }

    public ICollectionView ActivityLogView { get; }

    public ObservableCollection<QueueActionViewModel> RemainingQueue { get; }

    public bool ShowAllActivityLog
    {
        get => _selectedActivityLogFilter == ActivityLogFilter.All;
        set
        {
            if (value)
            {
                SetActivityLogFilter(ActivityLogFilter.All);
            }
        }
    }

    public bool ShowAlertsOnlyActivityLog
    {
        get => _selectedActivityLogFilter == ActivityLogFilter.AlertsOnly;
        set
        {
            if (value)
            {
                SetActivityLogFilter(ActivityLogFilter.AlertsOnly);
            }
        }
    }

    public bool ShowVerboseOnlyActivityLog
    {
        get => _selectedActivityLogFilter == ActivityLogFilter.VerboseOnly;
        set
        {
            if (value)
            {
                SetActivityLogFilter(ActivityLogFilter.VerboseOnly);
            }
        }
    }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public RelayCommand ToggleSyncCommand { get; }

    public RelayCommand BrowseSourcePathCommand { get; }

    public RelayCommand BrowseDestinationPathCommand { get; }

    public ParameterizedRelayCommand OpenPreviewItemCommand { get; }

    public ParameterizedRelayCommand OpenPreviewContainingFolderCommand { get; }

    public ParameterizedRelayCommand OpenPreviewFileCommand { get; }

    public ParameterizedRelayCommand OpenDestinationPreviewItemCommand { get; }

    public ParameterizedRelayCommand OpenDestinationPreviewContainingFolderCommand { get; }

    public ParameterizedRelayCommand OpenDestinationPreviewFileCommand { get; }

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                RaisePropertyChanged(nameof(SourcePathDisplayText));
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
                RaisePropertyChanged(nameof(DestinationPathDisplayText));
                HandleConfigurationChanged();
            }
        }
    }

    public string SourcePathDisplayText => _isSourcePathFocused
        ? SourcePath
        : _driveDisplayNameService.FormatPathForDisplay(SourcePath);

    public string DestinationPathDisplayText => _isDestinationPathFocused
        ? DestinationPath
        : _driveDisplayNameService.FormatPathForDisplay(DestinationPath);

    public SyncMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                RaisePropertyChanged(nameof(IsDetectMovesAvailable));
                RaisePropertyChanged(nameof(DirectionIndicator));
                RaisePropertyChanged(nameof(LeftDirectionIndicator));
                RaisePropertyChanged(nameof(RightDirectionIndicator));
                RaisePropertyChanged(nameof(SourceLocationDescription));
                RaisePropertyChanged(nameof(DestinationLocationDescription));
                HandleConfigurationChanged();
            }
        }
    }

    public string DirectionIndicator => SelectedMode switch
    {
        SyncMode.OneWay => ">>",
        SyncMode.TwoWay => "<>",
        _ => ">>",
    };

    public string LeftDirectionIndicator => SelectedMode switch
    {
        SyncMode.OneWay => ">",
        SyncMode.TwoWay => "<",
        _ => ">",
    };

    public string RightDirectionIndicator => ">";

    public string SourceLocationDescription => SelectedMode switch
    {
        SyncMode.OneWay => "This side is treated as the source of truth for one-way sync.",
        SyncMode.TwoWay => "Changes on this side are reconciled in both directions during two-way sync.",
        _ => "This side is treated as the source of truth for one-way sync.",
    };

    public string DestinationLocationDescription => SelectedMode switch
    {
        SyncMode.OneWay => "This side receives created folders, new files, and overwrite updates.",
        SyncMode.TwoWay => "Changes on this side are also reconciled back to the source location.",
        _ => "This side receives created folders, new files, and overwrite updates.",
    };

    public bool IsDetectMovesAvailable => SelectedMode == SyncMode.OneWay;

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

    public bool VerifyChecksums
    {
        get => _verifyChecksums;
        set
        {
            if (SetProperty(ref _verifyChecksums, value))
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
                ToggleSyncCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSyncRunning
    {
        get => _isSyncRunning;
        private set
        {
            if (SetProperty(ref _isSyncRunning, value))
            {
                RaisePropertyChanged(nameof(SyncButtonText));
                AnalyzeCommand.RaiseCanExecuteChanged();
                ToggleSyncCommand.RaiseCanExecuteChanged();
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
        private set
        {
            IsStatusSuccess = false;
            SetProperty(ref _statusMessage, value);
        }
    }

    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        private set => SetProperty(ref _isStatusSuccess, value);
    }

    private void SetStatusMessage(string message, bool isSuccess = false)
    {
        StatusMessage = message;
        IsStatusSuccess = isSuccess;
    }

    public string CurrentTransferItem
    {
        get => _currentTransferItem;
        private set => SetProperty(ref _currentTransferItem, value);
    }

    public string CurrentTransferDetails
    {
        get => _currentTransferDetails;
        private set => SetProperty(ref _currentTransferDetails, value);
    }

    public double CurrentTransferProgressValue
    {
        get => _currentTransferProgressValue;
        private set => SetProperty(ref _currentTransferProgressValue, value);
    }

    public int NewFilesCount => NewFiles.Count;

    public int ChangedFilesCount => ChangedFiles.Count;

    public int DeletedFilesCount => DeletedFiles.Count;

    public int UnchangedFilesCount => UnchangedFiles.Count;

    public int AllFilesCount => AllFiles.Count;

    public bool? AreAllNewFilesSelected
    {
        get => GetSelectionState(NewFiles);
        set => SetSelection(NewFiles, value);
    }

    public bool? AreAllChangedFilesSelected
    {
        get => GetSelectionState(ChangedFiles);
        set => SetSelection(ChangedFiles, value);
    }

    public bool? AreAllDeletedFilesSelected
    {
        get => GetSelectionState(DeletedFiles);
        set => SetSelection(DeletedFiles, value);
    }

    public bool? AreAllUnchangedFilesSelected
    {
        get => GetSelectionState(UnchangedFiles);
        set => SetSelection(UnchangedFiles, value);
    }

    public bool? AreAllFilesSelected
    {
        get => GetSelectionState(AllFiles);
        set => SetSelection(AllFiles, value);
    }

    public string QueueSummary => RemainingQueue.Count == 0
        ? "Queue empty"
        : $"{RemainingQueue.Count} queued item(s) remaining";

    public int ParallelCopyCount
    {
        get => _parallelCopyCount;
        set
        {
            var normalizedValue = Math.Max(0, value);
            if (SetProperty(ref _parallelCopyCount, normalizedValue))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public string SyncButtonText => IsSyncRunning ? "Stop synchronization" : "Synchronize";

    private static ISyncSettingsStore CreateDefaultSettingsStore()
    {
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UsbFileSync",
            "settings.json");

        return new JsonSyncSettingsStore(settingsPath);
    }

    private void BrowseSourcePath()
    {
        var selectedPath = _folderPickerService.PickFolder("Select the source drive or folder", SourcePath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SourcePath = selectedPath;
        }
    }

    private void BrowseDestinationPath()
    {
        var selectedPath = _folderPickerService.PickFolder("Select the destination drive or folder", DestinationPath);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DestinationPath = selectedPath;
        }
    }

    private void OpenPreviewItem(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenPath))
        {
            _fileLauncherService.OpenItem(row.OpenPath);
        }
    }

    private void OpenPreviewContainingFolder(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenPath))
        {
            _fileLauncherService.OpenContainingFolder(row.OpenPath);
        }
    }

    private void OpenPreviewFile(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && File.Exists(row.OpenPath))
        {
            _fileLauncherService.OpenFile(row.OpenPath);
        }
    }

    private void OpenDestinationPreviewItem(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenDestinationPath))
        {
            _fileLauncherService.OpenItem(row.OpenDestinationPath);
        }
    }

    private void OpenDestinationPreviewContainingFolder(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenDestinationPath))
        {
            _fileLauncherService.OpenContainingFolder(row.OpenDestinationPath);
        }
    }

    private void OpenDestinationPreviewFile(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel row && File.Exists(row.OpenDestinationPath))
        {
            _fileLauncherService.OpenFile(row.OpenDestinationPath);
        }
    }

    private static bool CanOpenPreviewItem(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenPath);

    private static bool CanOpenPreviewFile(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && File.Exists(row.OpenPath);

    private static bool CanOpenDestinationPreviewItem(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenDestinationPath);

    private static bool CanOpenDestinationPreviewFile(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && File.Exists(row.OpenDestinationPath);

    private bool CanExecuteSyncCommands() => !IsBusy && !IsSyncRunning && IsConfigurationComplete();

    private bool CanExecuteToggleSyncCommand() => IsSyncRunning || (!IsBusy && IsConfigurationComplete());

    private void ToggleSync()
    {
        if (IsSyncRunning)
        {
            _syncCancellationTokenSource?.Cancel();
            AddLog("Sync", "Stop requested by user.");
            StatusMessage = "Stopping synchronization...";
            return;
        }

        _ = StartSyncAsync();
    }

    private SyncConfiguration CreateConfiguration() => new()
    {
        SourcePath = SourcePath,
        DestinationPath = DestinationPath,
        Mode = SelectedMode,
        DetectMoves = DetectMoves,
        DryRun = DryRun,
        VerifyChecksums = VerifyChecksums,
        ParallelCopyCount = ParallelCopyCount,
    };

    public void UpdateParallelCopyCount(int parallelCopyCount)
    {
        ParallelCopyCount = parallelCopyCount;
        AddLog("Settings", ParallelCopyCount == 0
            ? "Parallel copy count set to auto."
            : $"Parallel copy count set to {ParallelCopyCount}.");
    }

    public void SetSourcePathFocused(bool isFocused)
    {
        if (SetProperty(ref _isSourcePathFocused, isFocused))
        {
            RaisePropertyChanged(nameof(SourcePathDisplayText));
        }
    }

    public void SetDestinationPathFocused(bool isFocused)
    {
        if (SetProperty(ref _isDestinationPathFocused, isFocused))
        {
            RaisePropertyChanged(nameof(DestinationPathDisplayText));
        }
    }

    private async Task AnalyzeAsync()
    {
        if (!TryValidateConfiguration(requireAccessibleDestinationPath: false))
        {
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        await RunBusyOperationAsync(async () =>
        {
            var configuration = CreateConfiguration();
            var actionsTask = _syncService.AnalyzeChangesAsync(configuration);
            var previewTask = _syncService.BuildPreviewAsync(configuration);
            await Task.WhenAll(actionsTask, previewTask).ConfigureAwait(true);
            var actions = actionsTask.Result;
            var preview = previewTask.Result;

            ReplaceActions(actions);
            ReplacePreview(preview);
            ReplaceQueue(GetSelectedActions());
            StatusMessage = actions.Count == 0
                ? "Folders are already synchronized."
                : $"Preview generated with {actions.Count} planned action(s). Select the items to synchronize.";
            ProgressValue = 0;
            CurrentTransferProgressValue = 0;
            CurrentTransferItem = actions.Count == 0 ? "No file operations required." : "Preview ready.";
            CurrentTransferDetails = QueueSummary;
            AddLog("Analyze", StatusMessage);
        }).ConfigureAwait(true);
    }

    private async Task StartSyncAsync()
    {
        if (!TryValidateConfiguration(requireAccessibleDestinationPath: true))
        {
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _syncCancellationTokenSource = cancellationTokenSource;
        IsSyncRunning = true;

        await RunBusyOperationAsync(async () =>
        {
            var configuration = CreateConfiguration();
            if (PlannedActions.Count == 0)
            {
                var initialActions = await _syncService.AnalyzeChangesAsync(configuration, cancellationTokenSource.Token).ConfigureAwait(true);
                var initialPreview = await _syncService.BuildPreviewAsync(configuration, cancellationTokenSource.Token).ConfigureAwait(true);
                ReplaceActions(initialActions);
                ReplacePreview(initialPreview);
                ReplaceQueue(GetSelectedActions());
            }

            var actions = RemainingQueue
                .Select(item => item.ActionModel)
                .ToList();

            if (actions.Count == 0)
            {
                StatusMessage = PlannedActions.Count == 0
                    ? "Folders are already synchronized."
                    : "Select at least one file or folder in the preview before synchronizing.";
                AddLog(
                    PlannedActions.Count == 0 ? "Sync" : "Warning",
                    PlannedActions.Count == 0
                        ? "No queued operations to process."
                        : "Synchronization skipped because no items to be synced were selected.",
                    PlannedActions.Count == 0 ? SyncLogSeverity.Verbose : SyncLogSeverity.Warning);
                CurrentTransferItem = "No active transfer.";
                CurrentTransferDetails = "Queue is idle.";
                CurrentTransferProgressValue = 0;
                ProgressValue = 0;
                return;
            }

            _queuedActions = actions;
            _completedQueuedActions = 0;
            CurrentTransferItem = actions[0].RelativePath;
            CurrentTransferDetails = QueueSummary;
            CurrentTransferProgressValue = 0;
            AddLog("Sync", $"Synchronization started with {actions.Count} queued operation(s).");

            var loggedTransferItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var progress = new Progress<SyncProgress>(update =>
            {
                while (_completedQueuedActions < update.CompletedOperations && _completedQueuedActions < _queuedActions.Count)
                {
                    var completedAction = _queuedActions[_completedQueuedActions];
                    if (RemainingQueue.Count > 0)
                    {
                        RemainingQueue.RemoveAt(0);
                        RaisePropertyChanged(nameof(QueueSummary));
                    }

                    AddLog("Done", $"{completedAction.Type}: {completedAction.RelativePath}");
                    MarkPreviewRowCompleted(completedAction.RelativePath);
                    _completedQueuedActions++;
                }

                if (ShouldLogCopyStart(loggedTransferItems, update))
                {
                    AddLog("Copy", $"Processing {update.CurrentItem}");
                }

                CurrentTransferItem = update.CurrentItem;
                UpdatePreviewRowProgress(update.CurrentItem, update.CurrentItemProgressPercentage, update.CurrentItemBytesTransferred);
                CurrentTransferProgressValue = update.CurrentItemProgressPercentage;
                CurrentTransferDetails = update.CurrentItemTotalBytes.HasValue
                    ? $"{update.CurrentItemBytesTransferred:n0} / {update.CurrentItemTotalBytes.Value:n0} bytes, {QueueSummary}"
                    : QueueSummary;
                ProgressValue = update.TotalOperations == 0
                    ? 0
                    : Math.Round(((double)update.CompletedOperations + (update.CurrentItemProgressPercentage / 100d)) / update.TotalOperations * 100, 0);
                StatusMessage = $"Processing {update.CurrentItem} ({update.CompletedOperations}/{update.TotalOperations}).";
            });
            var autoParallelism = new Progress<int>(effectiveParallelism =>
            {
                if (ParallelCopyCount == 0)
                {
                    AddLog("Copy", $"Auto parallel copy count adjusted to {effectiveParallelism}.");
                }
            });

            var result = await _syncService.ExecutePlannedAsync(configuration, actions, progress, autoParallelism, cancellationTokenSource.Token).ConfigureAwait(true);
            var verifiedCopyCount = configuration.VerifyChecksums
                ? actions.Count(action => action.Type is
                    SyncActionType.CopyToDestination or
                    SyncActionType.CopyToSource or
                    SyncActionType.OverwriteFileOnDestination or
                    SyncActionType.OverwriteFileOnSource)
                : 0;
            SetStatusMessage(
                result.IsDryRun
                    ? $"Dry run complete with {result.Actions.Count} planned action(s)."
                    : BuildCompletionMessage(result.AppliedOperations, configuration.VerifyChecksums, verifiedCopyCount),
                isSuccess: true);

            if (!result.IsDryRun)
            {
                ProgressValue = 100;
                CurrentTransferProgressValue = 100;
            }

            CurrentTransferDetails = QueueSummary;
            AddLog("Sync", StatusMessage);
            PlannedActions.Clear();
            ReplaceQueue(Array.Empty<SyncAction>());
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        if (ReferenceEquals(_syncCancellationTokenSource, cancellationTokenSource))
        {
            _syncCancellationTokenSource.Dispose();
            _syncCancellationTokenSource = null;
        }

        IsSyncRunning = false;
    }

    internal static bool ShouldLogCopyStart(ISet<string> loggedTransferItems, SyncProgress update)
    {
        return update.CompletedOperations < update.TotalOperations
            && !string.IsNullOrWhiteSpace(update.CurrentItem)
            && loggedTransferItems.Add(update.CurrentItem);
    }

    private void HandleConfigurationChanged()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        ToggleSyncCommand.RaiseCanExecuteChanged();

        if (_isLoadingSavedConfiguration)
        {
            return;
        }

        ScheduleConfigurationPersist();
    }

    private static string BuildCompletionMessage(int appliedOperations, bool verifyChecksums, int verifiedCopyCount)
    {
        var message = $"Synchronization complete. Applied {appliedOperations} action(s).";
        if (!verifyChecksums || verifiedCopyCount == 0)
        {
            return message;
        }

        return $"{message} Checksum verification passed for {verifiedCopyCount} copied file(s).";
    }

    private bool IsConfigurationComplete() =>
        !string.IsNullOrWhiteSpace(SourcePath) &&
        !string.IsNullOrWhiteSpace(DestinationPath) &&
        !string.Equals(SourcePath, DestinationPath, StringComparison.OrdinalIgnoreCase);

    private bool TryValidateConfiguration(bool requireAccessibleDestinationPath)
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            StatusMessage = "Choose a source drive path before running synchronization.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusMessage = "Choose a destination drive path before running synchronization.";
            return false;
        }

        if (string.Equals(SourcePath, DestinationPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Source and destination drive paths must be different.";
            return false;
        }

        if (!TryValidateSyncPath(SourcePath, "Source", requireExistingDirectory: true, out var sourceValidationMessage))
        {
            StatusMessage = sourceValidationMessage;
            return false;
        }

        if (requireAccessibleDestinationPath && !TryValidateSyncPath(DestinationPath, "Destination", requireExistingDirectory: false, out var destinationValidationMessage))
        {
            StatusMessage = destinationValidationMessage;
            return false;
        }

        return true;
    }

    private static bool TryValidateSyncPath(string path, string label, bool requireExistingDirectory, out string validationMessage)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            if (requireExistingDirectory && !Directory.Exists(fullPath))
            {
                validationMessage = $"{label} path does not exist.";
                return false;
            }

            if (!requireExistingDirectory && !HasAccessibleExistingAncestor(fullPath))
            {
                validationMessage = $"{label} path does not exist or is not accessible.";
                return false;
            }

            validationMessage = string.Empty;
            return true;
        }
        catch (Exception) when (path is not null)
        {
            validationMessage = $"{label} path is invalid.";
            return false;
        }
    }

    private static bool HasAccessibleExistingAncestor(string fullPath)
    {
        try
        {
            var current = fullPath;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current))
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent ?? string.Empty;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
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
            VerifyChecksums = savedConfiguration.VerifyChecksums;
            ParallelCopyCount = savedConfiguration.ParallelCopyCount;
            SetStatusMessage(
                IsConfigurationComplete()
                    ? "Restored the previous sync configuration."
                    : "Configure the source and destination drives to begin.",
                isSuccess: IsConfigurationComplete());
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
            ToggleSyncCommand.RaiseCanExecuteChanged();
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
        _syncCancellationTokenSource?.Cancel();
        _syncCancellationTokenSource?.Dispose();
        _syncCancellationTokenSource = null;

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

    private async Task RunBusyOperationAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Working...";
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Synchronization stopped.";
            CurrentTransferDetails = QueueSummary;
            CurrentTransferProgressValue = 0;
            PauseActivePreviewRows();
            AddLog("Warning", "Synchronization cancelled.", SyncLogSeverity.Warning);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            AddLog("Error", exception.Message, SyncLogSeverity.Error);
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

    private void ReplacePreview(IEnumerable<SyncPreviewItem> items)
    {
        foreach (var existingRow in _previewRowsByRelativePath.Values)
        {
            existingRow.PropertyChanged -= OnPreviewRowPropertyChanged;
        }

        var rows = items.Select(item => new SyncPreviewRowViewModel(item)).ToList();
        _previewRowsByRelativePath.Clear();
        foreach (var row in rows)
        {
            _previewRowsByRelativePath[row.RelativePath] = row;
            row.PropertyChanged += OnPreviewRowPropertyChanged;
        }

        ReplaceRows(NewFiles, rows.Where(row => row.Status == "New File"));
        ReplaceRows(ChangedFiles, rows.Where(row => row.Category == SyncPreviewCategory.ChangedFiles));
        ReplaceRows(DeletedFiles, rows.Where(row => row.Category == SyncPreviewCategory.DeletedFiles));
        ReplaceRows(UnchangedFiles, rows.Where(row => row.Status == "Unchanged"));
        ReplaceRows(AllFiles, rows);
        RaisePropertyChanged(nameof(NewFilesCount));
        RaisePropertyChanged(nameof(ChangedFilesCount));
        RaisePropertyChanged(nameof(DeletedFilesCount));
        RaisePropertyChanged(nameof(UnchangedFilesCount));
        RaisePropertyChanged(nameof(AllFilesCount));
        RaiseSelectionStateChanged();
    }

    private void ReplaceQueue(IEnumerable<SyncAction> actions)
    {
        RemainingQueue.Clear();
        foreach (var action in actions)
        {
            RemainingQueue.Add(new QueueActionViewModel(action));
        }

        RaisePropertyChanged(nameof(QueueSummary));
    }

    private IReadOnlyList<SyncAction> GetSelectedActions()
    {
        var selectedPaths = _previewRowsByRelativePath.Values
            .Where(row => row.CanSelect && row.IsSelected)
            .Select(row => row.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return PlannedActions
            .Where(action => selectedPaths.Contains(action.RelativePath))
            .ToList();
    }

    private bool? GetSelectionState(IEnumerable<SyncPreviewRowViewModel> rows)
    {
        var selectableRows = rows.Where(row => row.CanSelect).ToList();
        if (selectableRows.Count == 0)
        {
            return false;
        }

        var selectedCount = selectableRows.Count(row => row.IsSelected);
        if (selectedCount == 0)
        {
            return false;
        }

        if (selectedCount == selectableRows.Count)
        {
            return true;
        }

        return null;
    }

    private void SetSelection(IEnumerable<SyncPreviewRowViewModel> rows, bool? isSelected)
    {
        foreach (var row in rows.Where(row => row.CanSelect))
        {
                row.IsSelected = isSelected ?? false;
        }

        UpdateSelectedQueue();
    }

    private void OnPreviewRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncPreviewRowViewModel.IsSelected))
        {
            UpdateSelectedQueue();
        }
    }

    private void UpdateSelectedQueue()
    {
        ReplaceQueue(GetSelectedActions());
        RaiseSelectionStateChanged();
    }

    private void RaiseSelectionStateChanged()
    {
        RaisePropertyChanged(nameof(AreAllNewFilesSelected));
        RaisePropertyChanged(nameof(AreAllChangedFilesSelected));
        RaisePropertyChanged(nameof(AreAllDeletedFilesSelected));
        RaisePropertyChanged(nameof(AreAllUnchangedFilesSelected));
        RaisePropertyChanged(nameof(AreAllFilesSelected));
    }

    private void ReplaceRows(ObservableCollection<SyncPreviewRowViewModel> target, IEnumerable<SyncPreviewRowViewModel> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private void UpdatePreviewRowProgress(string relativePath, double progressValue, long bytesTransferred)
    {
        if (_previewRowsByRelativePath.TryGetValue(relativePath, out var row))
        {
            if (progressValue >= 100)
            {
                row.MarkCompleted();
                return;
            }

            row.MarkInProgress(progressValue, bytesTransferred, DateTime.UtcNow);
        }
    }

    private void MarkPreviewRowCompleted(string relativePath)
    {
        if (_previewRowsByRelativePath.TryGetValue(relativePath, out var row))
        {
            row.MarkCompleted();
        }
    }

    private void PauseActivePreviewRows()
    {
        foreach (var row in _previewRowsByRelativePath.Values)
        {
            if (row.ProgressStateText == "Transferring")
            {
                row.MarkPaused();
            }
        }
    }

    private void AddLog(string state, string message, SyncLogSeverity severity = SyncLogSeverity.Verbose)
    {
        ActivityLog.Insert(0, new SyncLogEntryViewModel(state, message, severity));
        const int maxLogEntries = 200;
        while (ActivityLog.Count > maxLogEntries)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }

        ActivityLogView.Refresh();
    }

    private void SetActivityLogFilter(ActivityLogFilter filter)
    {
        if (_selectedActivityLogFilter == filter)
        {
            return;
        }

        _selectedActivityLogFilter = filter;
        RaisePropertyChanged(nameof(ShowAllActivityLog));
        RaisePropertyChanged(nameof(ShowAlertsOnlyActivityLog));
        RaisePropertyChanged(nameof(ShowVerboseOnlyActivityLog));
        ActivityLogView.Refresh();
    }

    private bool ShouldIncludeActivityLogEntry(object item)
    {
        if (item is not SyncLogEntryViewModel entry)
        {
            return false;
        }

        return _selectedActivityLogFilter switch
        {
            ActivityLogFilter.AlertsOnly => entry.IsAlert,
            ActivityLogFilter.VerboseOnly => !entry.IsAlert,
            _ => true,
        };
    }

    private enum ActivityLogFilter
    {
        All,
        AlertsOnly,
        VerboseOnly,
    }
}
