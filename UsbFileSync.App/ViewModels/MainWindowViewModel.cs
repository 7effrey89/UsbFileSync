using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using UsbFileSync.App.Commands;
using UsbFileSync.App.Services;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App.ViewModels;

public enum PreviewTabKind
{
    NewFiles,
    ChangedFiles,
    DeletedFiles,
    UnchangedFiles,
    AllFiles,
}

public enum PreviewSelectionTarget
{
    FileName,
    FileFolder,
    FullPath,
}

internal enum BusyOperationKind
{
    None,
    Analyze,
    Sync,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromMilliseconds(250);

    private readonly SyncService _syncService;
    private readonly ISyncSettingsStore? _settingsStore;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ISourceVolumeService _sourceVolumeService;
    private readonly ISourceVolumeService _destinationVolumeService;
    private readonly ISyncExecutionClient _syncExecutionClient;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IDriveDisplayNameService _driveDisplayNameService;
    private readonly Dispatcher _dispatcher;
    private readonly bool _hasWpfApplication;
    private readonly object _activityLogLock = new();
    private readonly Dictionary<string, SyncPreviewRowViewModel> _previewRowsByItemKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PreviewTabKind, ICollectionView> _previewViews = new();
    private readonly Dictionary<PreviewColumnKey, HashSet<string>> _activePreviewFilters = new();
    private CancellationTokenSource? _persistConfigurationCancellationTokenSource;
    private CancellationTokenSource? _busyOperationCancellationTokenSource;
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
    private bool _hideMacOsSystemFiles = true;
    private Dictionary<string, string> _previewProviderMappings = PreviewProviderDefaults.CreateSerializableMapping();
    private bool _isBusy;
    private bool _isSyncRunning;
    private bool _isLoadingSavedConfiguration;
    private double _progressValue;
    private string _statusMessage = "Configure the source and destination drives to begin.";
    private bool _isStatusSuccess;
    private bool _isSourcePathFocused;
    private bool _isDestinationPathFocused;
    private BusyOperationKind _busyOperationKind;
    private string _busyOverlayTitle = "Working...";
    private string _busyOverlayDescription = "Please wait.";
    private bool _isDriveLocationColumnVisible;
    private PreviewColumnKey? _activePreviewFilterColumn;
    private PreviewColumnKey? _activePreviewSortColumn;
    private ListSortDirection _activePreviewSortDirection = ListSortDirection.Ascending;
    private string _previewFilterTitle = "Column filter";
    private string _previewFilterSearchText = string.Empty;
    private ActivityLogFilter _selectedActivityLogFilter = ActivityLogFilter.All;
    private bool _suppressSelectionUpdates;

    public MainWindowViewModel()
        : this(
            new SyncService(),
            CreateDefaultSettingsStore(),
            new WindowsFolderPickerService(),
            new WindowsFileLauncherService(),
            new WindowsDriveDisplayNameService(),
            SyncVolumeServiceFactory.CreateSourceVolumeService(),
            SyncVolumeServiceFactory.CreateDestinationVolumeService(),
            new WorkerSyncExecutionClient())
    {
    }

    public MainWindowViewModel(
        SyncService syncService,
        ISyncSettingsStore? settingsStore = null,
        IFolderPickerService? folderPickerService = null,
        IFileLauncherService? fileLauncherService = null,
        IDriveDisplayNameService? driveDisplayNameService = null,
        ISourceVolumeService? sourceVolumeService = null,
        ISourceVolumeService? destinationVolumeService = null,
        ISyncExecutionClient? syncExecutionClient = null)
    {
        _syncService = syncService;
        _settingsStore = settingsStore;
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _sourceVolumeService = sourceVolumeService ?? SyncVolumeServiceFactory.CreateSourceVolumeService();
        _destinationVolumeService = destinationVolumeService ?? SyncVolumeServiceFactory.CreateDestinationVolumeService();
        _syncExecutionClient = syncExecutionClient ?? new InProcessSyncExecutionClient(syncService);
        _fileLauncherService = fileLauncherService ?? new WindowsFileLauncherService();
        _driveDisplayNameService = driveDisplayNameService ?? new WindowsDriveDisplayNameService();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _hasWpfApplication = System.Windows.Application.Current is not null;
        AvailableModes = Enum.GetValues<SyncMode>();
        PlannedActions = new ObservableCollection<SyncAction>();
        AdditionalDestinationPaths = new ObservableCollection<DestinationPathEntryViewModel>();
        NewFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        ChangedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        DeletedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        UnchangedFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        AllFiles = new ObservableCollection<SyncPreviewRowViewModel>();
        if (_hasWpfApplication)
        {
            _previewViews[PreviewTabKind.NewFiles] = CreatePreviewView(NewFiles);
            _previewViews[PreviewTabKind.ChangedFiles] = CreatePreviewView(ChangedFiles);
            _previewViews[PreviewTabKind.DeletedFiles] = CreatePreviewView(DeletedFiles);
            _previewViews[PreviewTabKind.UnchangedFiles] = CreatePreviewView(UnchangedFiles);
            _previewViews[PreviewTabKind.AllFiles] = CreatePreviewView(AllFiles);
        }
        PreviewFilterOptions = new ObservableCollection<PreviewFilterOptionViewModel>();
        if (_hasWpfApplication)
        {
            PreviewFilterOptionsView = CollectionViewSource.GetDefaultView(PreviewFilterOptions);
            PreviewFilterOptionsView.Filter = ShouldIncludePreviewFilterOption;
        }
        ActivityLog = new ObservableCollection<SyncLogEntryViewModel>();
        BindingOperations.EnableCollectionSynchronization(ActivityLog, _activityLogLock);
        ActivityLogView = CollectionViewSource.GetDefaultView(ActivityLog);
        ActivityLogView.Filter = ShouldIncludeActivityLogEntry;
        RemainingQueue = new ObservableCollection<QueueActionViewModel>();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanExecuteSyncCommands);
        ToggleSyncCommand = new RelayCommand(ToggleSync, CanExecuteToggleSyncCommand);
        CancelBusyOperationCommand = new RelayCommand(CancelBusyOperation, CanCancelBusyOperation);
        BrowseSourcePathCommand = new RelayCommand(BrowseSourcePath);
        AddDestinationPathCommand = new RelayCommand(AddDestinationPath);
        BrowseDestinationPathCommand = new ParameterizedRelayCommand(BrowseDestinationPath);
        RemoveDestinationPathCommand = new ParameterizedRelayCommand(RemoveDestinationPath, CanRemoveDestinationPath);
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

    public ObservableCollection<DestinationPathEntryViewModel> AdditionalDestinationPaths { get; }

    public ObservableCollection<SyncPreviewRowViewModel> NewFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> ChangedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> DeletedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> UnchangedFiles { get; }

    public ObservableCollection<SyncPreviewRowViewModel> AllFiles { get; }

    public ObservableCollection<SyncLogEntryViewModel> ActivityLog { get; }

    public ICollectionView ActivityLogView { get; }

    public ObservableCollection<PreviewFilterOptionViewModel> PreviewFilterOptions { get; }

    public ICollectionView? PreviewFilterOptionsView { get; }

    public ObservableCollection<QueueActionViewModel> RemainingQueue { get; }

    public string PreviewFilterTitle
    {
        get => _previewFilterTitle;
        private set => SetProperty(ref _previewFilterTitle, value);
    }

    public string PreviewFilterSearchText
    {
        get => _previewFilterSearchText;
        set
        {
            if (SetProperty(ref _previewFilterSearchText, value))
            {
                PreviewFilterOptionsView?.Refresh();
            }
        }
    }

    public bool HasActivePreviewFilters => _activePreviewFilters.Count > 0;

    public bool IsDriveLocationColumnVisible
    {
        get => _isDriveLocationColumnVisible;
        private set => SetProperty(ref _isDriveLocationColumnVisible, value);
    }

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

    public RelayCommand CancelBusyOperationCommand { get; }

    public RelayCommand BrowseSourcePathCommand { get; }

    public RelayCommand AddDestinationPathCommand { get; }

    public ParameterizedRelayCommand BrowseDestinationPathCommand { get; }

    public ParameterizedRelayCommand RemoveDestinationPathCommand { get; }

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

    public bool HasAdditionalDestinationPaths => AdditionalDestinationPaths.Count > 0;

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

    public bool HideMacOsSystemFiles
    {
        get => _hideMacOsSystemFiles;
        set
        {
            if (SetProperty(ref _hideMacOsSystemFiles, value))
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
                RaisePropertyChanged(nameof(IsBusyOverlayVisible));
                AnalyzeCommand.RaiseCanExecuteChanged();
                ToggleSyncCommand.RaiseCanExecuteChanged();
                CancelBusyOperationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusyOverlayVisible => IsBusy && _busyOperationKind == BusyOperationKind.Analyze;

    public string BusyOverlayTitle
    {
        get => _busyOverlayTitle;
        private set => SetProperty(ref _busyOverlayTitle, value);
    }

    public string BusyOverlayDescription
    {
        get => _busyOverlayDescription;
        private set => SetProperty(ref _busyOverlayDescription, value);
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
        get => GetSelectionState(PreviewTabKind.NewFiles);
        set => SetSelection(PreviewTabKind.NewFiles, value);
    }

    public bool? AreAllChangedFilesSelected
    {
        get => GetSelectionState(PreviewTabKind.ChangedFiles);
        set => SetSelection(PreviewTabKind.ChangedFiles, value);
    }

    public bool? AreAllDeletedFilesSelected
    {
        get => GetSelectionState(PreviewTabKind.DeletedFiles);
        set => SetSelection(PreviewTabKind.DeletedFiles, value);
    }

    public bool? AreAllUnchangedFilesSelected
    {
        get => GetSelectionState(PreviewTabKind.UnchangedFiles);
        set => SetSelection(PreviewTabKind.UnchangedFiles, value);
    }

    public bool? AreAllFilesSelected
    {
        get => GetSelectionState(PreviewTabKind.AllFiles);
        set => SetSelection(PreviewTabKind.AllFiles, value);
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
        var selectedPath = WindowsSourceLocationPickerService.PickSourceLocation(SourcePath, _folderPickerService, _sourceVolumeService);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SourcePath = selectedPath;
        }
    }

    private void AddDestinationPath()
    {
        AdditionalDestinationPaths.Add(CreateDestinationPathEntry(string.Empty));
        RaisePropertyChanged(nameof(HasAdditionalDestinationPaths));
        HandleConfigurationChanged();
    }

    private void BrowseDestinationPath(object? parameter)
    {
        var destinationPath = parameter is DestinationPathEntryViewModel entry
            ? entry.Path
            : DestinationPath;
        var selectedPath = WindowsSourceLocationPickerService.PickDestinationLocation(destinationPath, _folderPickerService, _destinationVolumeService);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            if (parameter is DestinationPathEntryViewModel destinationEntry)
            {
                destinationEntry.Path = selectedPath;
            }
            else
            {
                DestinationPath = selectedPath;
            }
        }
    }

    private void RemoveDestinationPath(object? parameter)
    {
        if (parameter is not DestinationPathEntryViewModel entry)
        {
            return;
        }

        if (AdditionalDestinationPaths.Remove(entry))
        {
            RaisePropertyChanged(nameof(HasAdditionalDestinationPaths));
            HandleConfigurationChanged();
        }
    }

    private static bool CanRemoveDestinationPath(object? parameter) => parameter is DestinationPathEntryViewModel;

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

    private bool CanCancelBusyOperation() => _busyOperationCancellationTokenSource is { IsCancellationRequested: false };

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

    private void CancelBusyOperation()
    {
        var cancellationTokenSource = _busyOperationCancellationTokenSource;
        if (cancellationTokenSource is null || cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        cancellationTokenSource.Cancel();

        switch (_busyOperationKind)
        {
            case BusyOperationKind.Analyze:
                StatusMessage = "Cancelling preview generation...";
                AddLog("Analyze", "Cancel requested while building the preview.", SyncLogSeverity.Warning);
                break;
            case BusyOperationKind.Sync:
                StatusMessage = "Stopping synchronization...";
                AddLog("Sync", "Stop requested by user.");
                break;
        }
    }

    private SyncConfiguration CreateConfiguration()
    {
        return SyncVolumeServiceFactory.ResolveConfiguration(
            new SyncConfiguration
            {
                SourcePath = SourcePath,
                DestinationPath = DestinationPath,
                DestinationPaths = GetDestinationPaths().ToList(),
                Mode = SelectedMode,
                DetectMoves = DetectMoves,
                DryRun = DryRun,
                VerifyChecksums = VerifyChecksums,
                HideMacOsSystemFiles = HideMacOsSystemFiles,
                ParallelCopyCount = ParallelCopyCount,
                PreviewProviderMappings = new Dictionary<string, string>(_previewProviderMappings, StringComparer.OrdinalIgnoreCase),
            },
            _sourceVolumeService,
            _destinationVolumeService);
    }

    public void UpdateParallelCopyCount(int parallelCopyCount)
    {
        ParallelCopyCount = parallelCopyCount;
        AddLog("Settings", ParallelCopyCount == 0
            ? "Parallel copy count set to auto."
            : $"Parallel copy count set to {ParallelCopyCount}.");
    }

    public IReadOnlyDictionary<string, string> GetPreviewProviderMappings() =>
        new Dictionary<string, string>(_previewProviderMappings, StringComparer.OrdinalIgnoreCase);

    public void UpdatePreviewProviderMappings(IReadOnlyDictionary<string, string> mappings)
    {
        _previewProviderMappings = new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase);
        HandleConfigurationChanged();
        AddLog("Settings", $"Preview provider mappings updated for {_previewProviderMappings.Count} file extensions.");
    }

    public void UpdateHideMacOsSystemFiles(bool hideMacOsSystemFiles)
    {
        HideMacOsSystemFiles = hideMacOsSystemFiles;
        AddLog("Settings", HideMacOsSystemFiles
            ? "HFS+ macOS system files will be hidden from preview and sync."
            : "HFS+ macOS system files will be shown in preview and included in sync planning.");
    }

    public void SelectAllInTab(PreviewTabKind tabKind)
    {
        UpdateSelectionForRows(GetVisibleRowsForTab(tabKind), _ => true);
    }

    public void InvertSelectionInTab(PreviewTabKind tabKind)
    {
        UpdateSelectionForRows(GetVisibleRowsForTab(tabKind), row => !row.IsSelected);
    }

    public int SelectByPattern(PreviewTabKind tabKind, string keyword, PreviewSelectionTarget target)
    {
        var normalizedKeyword = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return 0;
        }

        var rows = GetRowsForTab(tabKind)
            .Where(row => row.CanSelect)
            .ToList();

        if (rows.Count == 0)
        {
            return 0;
        }

        UpdateSelectionForRows(rows, row => MatchesPattern(row, normalizedKeyword, target));
        return rows.Count(row => row.IsSelected);
    }

    public void OpenPreviewColumnFilter(PreviewTabKind tabKind, PreviewColumnHeader header)
    {
        if (header.ColumnKey == PreviewColumnKey.DriveLocation && !IsDriveLocationColumnVisible)
        {
            return;
        }

        _activePreviewFilterColumn = header.ColumnKey;
        PreviewFilterTitle = header.Title;
        PreviewFilterSearchText = string.Empty;

        var selectedValues = _activePreviewFilters.TryGetValue(header.ColumnKey, out var activeValues)
            ? activeValues
            : null;

        var optionValues = GetRowsForFilterOptions(tabKind, header.ColumnKey)
            .Select(row => GetPreviewColumnValue(row, header.ColumnKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PreviewFilterOptions.Clear();
        foreach (var optionValue in optionValues)
        {
            PreviewFilterOptions.Add(new PreviewFilterOptionViewModel(
                optionValue,
                selectedValues is null || selectedValues.Contains(optionValue)));
        }

        PreviewFilterOptionsView?.Refresh();
    }

    public void ApplyActivePreviewColumnFilter()
    {
        if (!_activePreviewFilterColumn.HasValue)
        {
            return;
        }

        var column = _activePreviewFilterColumn.Value;
        var selectedValues = PreviewFilterOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedValues.Count == PreviewFilterOptions.Count)
        {
            _activePreviewFilters.Remove(column);
        }
        else
        {
            _activePreviewFilters[column] = selectedValues;
        }

        RefreshPreviewViews();
        RaisePropertyChanged(nameof(HasActivePreviewFilters));
        RaiseSelectionStateChanged();
    }

    public void ClearActivePreviewColumnFilter()
    {
        if (!_activePreviewFilterColumn.HasValue)
        {
            return;
        }

        _activePreviewFilters.Remove(_activePreviewFilterColumn.Value);
        foreach (var option in PreviewFilterOptions)
        {
            option.IsSelected = true;
        }

        RefreshPreviewViews();
        RaisePropertyChanged(nameof(HasActivePreviewFilters));
        RaiseSelectionStateChanged();
    }

    public void SetAllVisiblePreviewFilterOptions(bool isSelected)
    {
        var options = GetShownPreviewFilterOptions();

        foreach (var option in options)
        {
            option.IsSelected = isSelected;
        }
    }

    public void SetAllNonShownPreviewFilterOptions(bool isSelected)
    {
        foreach (var option in GetNonShownPreviewFilterOptions())
        {
            option.IsSelected = isSelected;
        }
    }

    public void SortActivePreviewColumn(ListSortDirection direction)
    {
        if (!_activePreviewFilterColumn.HasValue)
        {
            return;
        }

        _activePreviewSortColumn = _activePreviewFilterColumn.Value;
        _activePreviewSortDirection = direction;
        ApplyPreviewSort();
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

    public void SetAdditionalDestinationPathFocused(object? parameter, bool isFocused)
    {
        if (parameter is DestinationPathEntryViewModel entry)
        {
            entry.SetFocused(isFocused);
        }
    }

    private async Task AnalyzeAsync()
    {
        if (!TryValidateConfiguration(requireAccessibleDestinationPath: false))
        {
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.Analyze);

        await RunBusyOperationAsync(async () =>
        {
            var configuration = CreateConfiguration();
            await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);

            var analysisResult = await Task.Run(async () =>
            {
                var actions = await _syncService.AnalyzeChangesAsync(configuration, cancellationTokenSource.Token).ConfigureAwait(false);
                var preview = _syncService.BuildPreview(configuration, actions, cancellationTokenSource.Token);
                return new AnalyzePreviewResult(actions, preview);
            }, cancellationTokenSource.Token).ConfigureAwait(true);

            var actions = analysisResult.Actions;
            var preview = analysisResult.Preview;

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
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        ClearBusyOperation(cancellationTokenSource);
        cancellationTokenSource.Dispose();
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
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.Sync);
        IsSyncRunning = true;

        await RunBusyOperationAsync(async () =>
        {
            var configuration = CreateConfiguration();
            if (PlannedActions.Count == 0)
            {
                await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);

                var initialAnalysis = await Task.Run(async () =>
                {
                    var initialActions = await _syncService.AnalyzeChangesAsync(configuration, cancellationTokenSource.Token).ConfigureAwait(false);
                    var initialPreview = _syncService.BuildPreview(configuration, initialActions, cancellationTokenSource.Token);
                    return new AnalyzePreviewResult(initialActions, initialPreview);
                }, cancellationTokenSource.Token).ConfigureAwait(true);

                var initialActions = initialAnalysis.Actions;
                var initialPreview = initialAnalysis.Preview;
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
                    MarkPreviewRowCompleted(completedAction.GetActionKey());
                    _completedQueuedActions++;
                }

                if (ShouldLogCopyStart(loggedTransferItems, update))
                {
                    AddLog("Copy", $"Processing {update.CurrentItem}");
                }

                CurrentTransferItem = update.CurrentItem;
                UpdatePreviewRowProgress(update.CurrentItemKey ?? update.CurrentItem, update.CurrentItemProgressPercentage, update.CurrentItemBytesTransferred);
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

            var result = await _syncExecutionClient.ExecuteAsync(configuration, actions, progress, autoParallelism, cancellationTokenSource.Token).ConfigureAwait(true);
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

            if (result.IsDryRun)
            {
                ReplaceQueue(GetSelectedActions());
            }
            else
            {
                RetireCompletedActionsFromPreview(actions);
                ReplaceQueue(GetSelectedActions());
            }
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        if (ReferenceEquals(_syncCancellationTokenSource, cancellationTokenSource))
        {
            _syncCancellationTokenSource = null;
        }

        ClearBusyOperation(cancellationTokenSource);
        cancellationTokenSource.Dispose();

        IsSyncRunning = false;
    }

    internal static bool ShouldLogCopyStart(ISet<string> loggedTransferItems, SyncProgress update)
    {
        return update.CompletedOperations < update.TotalOperations
            && !string.IsNullOrWhiteSpace(update.CurrentItem)
            && loggedTransferItems.Add(update.CurrentItemKey ?? update.CurrentItem);
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

    private IReadOnlyList<string> GetDestinationPaths()
    {
        var destinationPaths = new List<string>();
        if (!string.IsNullOrWhiteSpace(DestinationPath))
        {
            destinationPaths.Add(DestinationPath);
        }

        destinationPaths.AddRange(AdditionalDestinationPaths
            .Select(entry => entry.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path)));

        return destinationPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DestinationPathEntryViewModel CreateDestinationPathEntry(string path) =>
        new(path, _driveDisplayNameService, HandleConfigurationChanged);

    private void LoadDestinationPaths(IReadOnlyList<string> destinationPaths)
    {
        var normalizedDestinationPaths = destinationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DestinationPath = normalizedDestinationPaths.FirstOrDefault() ?? string.Empty;

        AdditionalDestinationPaths.Clear();
        foreach (var destinationPath in normalizedDestinationPaths.Skip(1))
        {
            AdditionalDestinationPaths.Add(CreateDestinationPathEntry(destinationPath));
        }

        RaisePropertyChanged(nameof(HasAdditionalDestinationPaths));
    }

    private bool IsConfigurationComplete() =>
        !string.IsNullOrWhiteSpace(SourcePath) &&
        GetDestinationPaths().Count > 0 &&
        GetDestinationPaths().All(destinationPath => !string.Equals(SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase));

    private bool TryValidateConfiguration(bool requireAccessibleDestinationPath)
    {
        var destinationPaths = GetDestinationPaths();
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            StatusMessage = "Choose a source drive path before running synchronization.";
            return false;
        }

        if (destinationPaths.Count == 0)
        {
            StatusMessage = "Choose a destination drive path before running synchronization.";
            return false;
        }

        if (destinationPaths.Any(destinationPath => string.Equals(SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = destinationPaths.Count == 1
                ? "Source and destination drive paths must be different."
                : "Source and destination drive paths must all be different.";
            return false;
        }

        if (destinationPaths.Count != destinationPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            StatusMessage = "Destination drive paths must be unique.";
            return false;
        }

        if (!TryValidateSyncPath(SourcePath, "Source", requireExistingDirectory: true, out var sourceValidationMessage))
        {
            StatusMessage = sourceValidationMessage;
            return false;
        }

        if (!requireAccessibleDestinationPath)
        {
            return true;
        }

        for (var index = 0; index < destinationPaths.Count; index++)
        {
            var label = destinationPaths.Count == 1
                ? "Destination"
                : $"Destination {index + 1}";
            if (!TryValidateDestinationPath(destinationPaths[index], label, out var destinationValidationMessage))
            {
                StatusMessage = destinationValidationMessage;
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateSyncPathCore(string path, string label, bool requireExistingDirectory, out string validationMessage)
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

    private bool TryValidateSyncPath(string path, string label, bool requireExistingDirectory, out string validationMessage)
    {
        if (!string.Equals(label, "Source", StringComparison.OrdinalIgnoreCase) || !requireExistingDirectory)
        {
            return TryValidateSyncPathCore(path, label, requireExistingDirectory, out validationMessage);
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                validationMessage = string.Empty;
                return true;
            }
        }
        catch (Exception) when (path is not null)
        {
            if (_sourceVolumeService.TryCreateVolume(path, out _, out var sourceFailureReason))
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage = string.IsNullOrWhiteSpace(sourceFailureReason)
                ? $"{label} path is invalid."
                : $"{label} macOS volume could not be opened. {sourceFailureReason}";
            return false;
        }

        if (_sourceVolumeService.TryCreateVolume(path, out _, out var failureReason))
        {
            validationMessage = string.Empty;
            return true;
        }

        validationMessage = string.IsNullOrWhiteSpace(failureReason)
            ? $"{label} path does not exist."
            : $"{label} macOS volume could not be opened. {failureReason}";
        return false;
    }

    private bool TryValidateDestinationPath(string path, string label, out string validationMessage)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (HasAccessibleExistingAncestor(fullPath))
            {
                validationMessage = string.Empty;
                return true;
            }
        }
        catch (Exception) when (path is not null)
        {
        }

        if (_destinationVolumeService.TryCreateVolume(path, out var volume, out var failureReason))
        {
            if (volume is not null &&
                string.Equals(volume.FileSystemType, "ext4", StringComparison.OrdinalIgnoreCase) &&
                volume.IsReadOnly)
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage = string.Empty;
            return true;
        }

        validationMessage = string.IsNullOrWhiteSpace(failureReason)
            ? $"{label} path does not exist or is not accessible."
            : $"{label} Linux volume could not be opened. {failureReason}";
        return false;
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
            LoadDestinationPaths(savedConfiguration.GetDestinationPaths());
            SelectedMode = savedConfiguration.Mode;
            DetectMoves = savedConfiguration.DetectMoves;
            DryRun = savedConfiguration.DryRun;
            VerifyChecksums = savedConfiguration.VerifyChecksums;
            HideMacOsSystemFiles = savedConfiguration.HideMacOsSystemFiles;
            ParallelCopyCount = savedConfiguration.ParallelCopyCount;
            _previewProviderMappings = savedConfiguration.PreviewProviderMappings?.Count > 0
                ? new Dictionary<string, string>(savedConfiguration.PreviewProviderMappings, StringComparer.OrdinalIgnoreCase)
                : PreviewProviderDefaults.CreateSerializableMapping();
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
        if (_syncExecutionClient is IDisposable disposableSyncExecutionClient)
        {
            disposableSyncExecutionClient.Dispose();
        }

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
            StatusMessage = _busyOperationKind switch
            {
                BusyOperationKind.Analyze => "Preview loading cancelled.",
                BusyOperationKind.Sync => "Synchronization stopped.",
                _ => "Operation cancelled.",
            };
            CurrentTransferItem = _busyOperationKind switch
            {
                BusyOperationKind.Analyze => "Preview generation cancelled.",
                BusyOperationKind.Sync => "No active transfer.",
                _ => CurrentTransferItem,
            };
            CurrentTransferDetails = QueueSummary;
            CurrentTransferProgressValue = 0;
            if (_busyOperationKind == BusyOperationKind.Sync)
            {
                PauseActivePreviewRows();
            }

            AddLog(
                "Warning",
                _busyOperationKind == BusyOperationKind.Analyze
                    ? "Preview generation cancelled."
                    : "Synchronization cancelled.",
                SyncLogSeverity.Warning);
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

    private static async Task EnsureBusyOverlayCanRenderAsync()
    {
        if (System.Windows.Application.Current is null)
        {
            await Task.Yield();
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private sealed record AnalyzePreviewResult(
        IReadOnlyList<SyncAction> Actions,
        IReadOnlyList<SyncPreviewItem> Preview);

    private void ReplaceActions(IEnumerable<SyncAction> actions)
    {
        RunOnDispatcher(() =>
        {
            PlannedActions.Clear();
            foreach (var action in actions)
            {
                PlannedActions.Add(action);
            }
        });
    }

    private void ReplacePreview(IEnumerable<SyncPreviewItem> items)
    {
        var previewItems = items.ToList();

        RunOnDispatcher(() =>
        {
            foreach (var existingRow in _previewRowsByItemKey.Values)
            {
                existingRow.PropertyChanged -= OnPreviewRowPropertyChanged;
            }

            var rows = previewItems
                .Select(item => new SyncPreviewRowViewModel(item, driveDisplayNameService: _driveDisplayNameService))
                .ToList();

            rows = SortPreviewRows(rows);
            _previewRowsByItemKey.Clear();
            foreach (var row in rows)
            {
                _previewRowsByItemKey[row.ItemKey] = row;
                row.PropertyChanged += OnPreviewRowPropertyChanged;
            }

            UpdateDriveLocationColumnVisibility(rows);

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
        });
    }

    private void ReplaceQueue(IEnumerable<SyncAction> actions)
    {
        var queueActions = actions.ToList();

        RunOnDispatcher(() =>
        {
            RemainingQueue.Clear();
            foreach (var action in queueActions)
            {
                RemainingQueue.Add(new QueueActionViewModel(action));
            }

            RaisePropertyChanged(nameof(QueueSummary));
        });
    }

    private void RetireCompletedActionsFromPreview(IEnumerable<SyncAction> actions)
    {
        var completedKeys = actions
            .Select(action => action.GetActionKey())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (completedKeys.Count == 0)
        {
            return;
        }

        RunOnDispatcher(() =>
        {
            foreach (var action in PlannedActions.Where(action => completedKeys.Contains(action.GetActionKey())).ToList())
            {
                PlannedActions.Remove(action);
            }

            _suppressSelectionUpdates = true;
            try
            {
                foreach (var completedKey in completedKeys)
                {
                    if (_previewRowsByItemKey.TryGetValue(completedKey, out var row))
                    {
                        row.MarkApplied();
                    }
                }
            }
            finally
            {
                _suppressSelectionUpdates = false;
            }

            UpdateSelectedQueue();
        });
    }

    private IReadOnlyList<SyncAction> GetSelectedActions()
    {
        var selectedKeys = _previewRowsByItemKey.Values
            .Where(row => row.CanSelect && row.IsSelected)
            .Select(row => row.ItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return PlannedActions
            .Where(action => selectedKeys.Contains(action.GetActionKey()))
            .ToList();
    }

    private bool? GetSelectionState(PreviewTabKind tabKind)
    {
        var selectableRows = GetVisibleRowsForTab(tabKind)
            .Where(row => row.CanSelect)
            .ToList();
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

    private void SetSelection(PreviewTabKind tabKind, bool? isSelected)
    {
        UpdateSelectionForRows(GetVisibleRowsForTab(tabKind), _ => isSelected ?? false);
    }

    private void OnPreviewRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressSelectionUpdates &&
            (e.PropertyName == nameof(SyncPreviewRowViewModel.IsSelected) ||
             e.PropertyName == nameof(SyncPreviewRowViewModel.CanSelect)))
        {
            UpdateSelectedQueue();
            return;
        }

        if (_activePreviewFilters.Count > 0)
        {
            RefreshPreviewViews();
            RaiseSelectionStateChanged();
        }
    }

    private IReadOnlyList<SyncPreviewRowViewModel> GetRowsForTab(PreviewTabKind tabKind) => tabKind switch
    {
        PreviewTabKind.NewFiles => NewFiles,
        PreviewTabKind.ChangedFiles => ChangedFiles,
        PreviewTabKind.DeletedFiles => DeletedFiles,
        PreviewTabKind.UnchangedFiles => UnchangedFiles,
        _ => AllFiles,
    };

    private IReadOnlyList<SyncPreviewRowViewModel> GetVisibleRowsForTab(PreviewTabKind tabKind)
    {
        if (_previewViews.TryGetValue(tabKind, out var view))
        {
            return view.Cast<SyncPreviewRowViewModel>().ToList();
        }

        return GetRowsForTab(tabKind);
    }

    private IEnumerable<SyncPreviewRowViewModel> GetRowsForFilterOptions(PreviewTabKind tabKind, PreviewColumnKey columnKey) =>
        GetRowsForTab(tabKind).Where(row => MatchesActivePreviewFilters(row, columnKey));

    private void UpdateSelectionForRows(IEnumerable<SyncPreviewRowViewModel> rows, Func<SyncPreviewRowViewModel, bool> selectionFactory)
    {
        var selectableRows = rows
            .Where(row => row.CanSelect)
            .ToList();

        if (selectableRows.Count == 0)
        {
            return;
        }

        _suppressSelectionUpdates = true;
        try
        {
            foreach (var row in selectableRows)
            {
                row.IsSelected = selectionFactory(row);
            }
        }
        finally
        {
            _suppressSelectionUpdates = false;
        }

        UpdateSelectedQueue();
    }

    private static bool MatchesPattern(SyncPreviewRowViewModel row, string keyword, PreviewSelectionTarget target)
    {
        return target switch
        {
            PreviewSelectionTarget.FileName => GetPathCandidates(row)
                .Select(Path.GetFileName)
                .Any(value => ContainsKeyword(value, keyword)),
            PreviewSelectionTarget.FileFolder => GetPathCandidates(row)
                .Select(Path.GetDirectoryName)
                .Any(value => ContainsKeyword(value, keyword)),
            PreviewSelectionTarget.FullPath => GetPathCandidates(row)
                .Any(value => ContainsKeyword(value, keyword)),
            _ => false,
        };
    }

    private static IEnumerable<string> GetPathCandidates(SyncPreviewRowViewModel row)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(row.SourcePath))
        {
            paths.Add(row.SourcePath);
        }

        if (!string.IsNullOrWhiteSpace(row.DestinationPath))
        {
            paths.Add(row.DestinationPath);
        }

        if (!string.IsNullOrWhiteSpace(row.Name))
        {
            paths.Add(row.Name);
        }

        return paths;
    }

    private static bool ContainsKeyword(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

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

    private ICollectionView CreatePreviewView(ObservableCollection<SyncPreviewRowViewModel> rows)
    {
        var view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = ShouldIncludePreviewRow;
        return view;
    }

    private void RefreshPreviewViews()
    {
        foreach (var view in _previewViews.Values)
        {
            if (view is not DispatcherObject dispatcherObject || dispatcherObject.Dispatcher.CheckAccess())
            {
                view.Refresh();
            }
            else
            {
                dispatcherObject.Dispatcher.BeginInvoke(view.Refresh);
            }
        }
    }

    private IEnumerable<PreviewFilterOptionViewModel> GetShownPreviewFilterOptions() =>
        PreviewFilterOptions.Where(ShouldIncludePreviewFilterOption);

    private IEnumerable<PreviewFilterOptionViewModel> GetNonShownPreviewFilterOptions() =>
        PreviewFilterOptions.Where(option => !ShouldIncludePreviewFilterOption(option));

    private void ApplyPreviewSort()
    {
        ReplaceRows(NewFiles, SortPreviewRows(NewFiles));
        ReplaceRows(ChangedFiles, SortPreviewRows(ChangedFiles));
        ReplaceRows(DeletedFiles, SortPreviewRows(DeletedFiles));
        ReplaceRows(UnchangedFiles, SortPreviewRows(UnchangedFiles));
        ReplaceRows(AllFiles, SortPreviewRows(AllFiles));
        RefreshPreviewViews();
    }

    private List<SyncPreviewRowViewModel> SortPreviewRows(IEnumerable<SyncPreviewRowViewModel> rows)
    {
        var rowList = rows.ToList();
        if (!_activePreviewSortColumn.HasValue)
        {
            return rowList;
        }

        rowList.Sort(ComparePreviewRows);
        return rowList;
    }

    private int ComparePreviewRows(SyncPreviewRowViewModel? left, SyncPreviewRowViewModel? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var column = _activePreviewSortColumn ?? PreviewColumnKey.SourceFile;
        var result = column switch
        {
            PreviewColumnKey.SyncAction => CompareSyncActions(left, right),
            PreviewColumnKey.SourceSize => Nullable.Compare(left.SourceSizeBytes, right.SourceSizeBytes),
            PreviewColumnKey.DestinationSize => Nullable.Compare(left.DestinationSizeBytes, right.DestinationSizeBytes),
            PreviewColumnKey.SourceModified => Nullable.Compare(left.SourceModifiedUtc, right.SourceModifiedUtc),
            PreviewColumnKey.DestinationModified => Nullable.Compare(left.DestinationModifiedUtc, right.DestinationModifiedUtc),
            _ => StringComparer.OrdinalIgnoreCase.Compare(GetPreviewColumnValue(left, column), GetPreviewColumnValue(right, column)),
        };

        if (result == 0)
        {
            result = StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
        }

        return _activePreviewSortDirection == ListSortDirection.Ascending ? result : -result;
    }

    private static int CompareSyncActions(SyncPreviewRowViewModel left, SyncPreviewRowViewModel right)
    {
        var actionResult = StringComparer.OrdinalIgnoreCase.Compare(left.SyncActionDisplayText, right.SyncActionDisplayText);
        if (actionResult != 0)
        {
            return actionResult;
        }

        var directionResult = StringComparer.OrdinalIgnoreCase.Compare(left.Direction, right.Direction);
        if (directionResult != 0)
        {
            return directionResult;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Action, right.Action);
    }

    private void UpdateDriveLocationColumnVisibility(IReadOnlyCollection<SyncPreviewRowViewModel> rows)
    {
        var shouldShowDriveLocationColumn = rows
            .Select(row => row.DriveLocation)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count() > 1;

        if (IsDriveLocationColumnVisible == shouldShowDriveLocationColumn)
        {
            return;
        }

        IsDriveLocationColumnVisible = shouldShowDriveLocationColumn;

        if (shouldShowDriveLocationColumn)
        {
            return;
        }

        _activePreviewFilters.Remove(PreviewColumnKey.DriveLocation);
        if (_activePreviewFilterColumn == PreviewColumnKey.DriveLocation)
        {
            _activePreviewFilterColumn = null;
            PreviewFilterOptions.Clear();
            PreviewFilterSearchText = string.Empty;
        }

        RaisePropertyChanged(nameof(HasActivePreviewFilters));
        RefreshPreviewViews();
    }

    private bool ShouldIncludePreviewRow(object item) =>
        item is SyncPreviewRowViewModel row && MatchesActivePreviewFilters(row);

    private bool MatchesActivePreviewFilters(SyncPreviewRowViewModel row, PreviewColumnKey? excludedColumn = null)
    {
        foreach (var filter in _activePreviewFilters)
        {
            if (excludedColumn.HasValue && filter.Key == excludedColumn.Value)
            {
                continue;
            }

            if (!filter.Value.Contains(GetPreviewColumnValue(row, filter.Key)))
            {
                return false;
            }
        }

        return true;
    }

    private bool ShouldIncludePreviewFilterOption(object item)
    {
        return item is PreviewFilterOptionViewModel option && ShouldIncludePreviewFilterOption(option);
    }

    private bool ShouldIncludePreviewFilterOption(PreviewFilterOptionViewModel option)
    {
        return string.IsNullOrWhiteSpace(PreviewFilterSearchText)
            || option.Value.Contains(PreviewFilterSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPreviewColumnValue(SyncPreviewRowViewModel row, PreviewColumnKey columnKey) => NormalizePreviewFilterValue(columnKey switch
    {
        PreviewColumnKey.SourceFile => row.SourcePath,
        PreviewColumnKey.SyncAction => row.SyncActionDisplayText,
        PreviewColumnKey.TransferSpeed => row.TransferSpeedText,
        PreviewColumnKey.DriveLocation => row.DriveLocation,
        PreviewColumnKey.DestinationFile => row.DestinationPath,
        PreviewColumnKey.FileType => row.FileType,
        PreviewColumnKey.SourceSize => row.SourceSize,
        PreviewColumnKey.DestinationSize => row.DestinationSize,
        PreviewColumnKey.SourceModified => row.SourceModified,
        PreviewColumnKey.DestinationModified => row.DestinationModified,
        PreviewColumnKey.Status => row.Status,
        PreviewColumnKey.Action => row.Action,
        _ => string.Empty,
    });

    private static string NormalizePreviewFilterValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();

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
        RunOnDispatcher(() =>
        {
            if (_previewRowsByItemKey.TryGetValue(relativePath, out var row))
            {
                if (progressValue >= 100)
                {
                    row.MarkCompleted();
                    return;
                }

                row.MarkInProgress(progressValue, bytesTransferred, DateTime.UtcNow);
            }
        });
    }

    private void MarkPreviewRowCompleted(string relativePath)
    {
        RunOnDispatcher(() =>
        {
            if (_previewRowsByItemKey.TryGetValue(relativePath, out var row))
            {
                row.MarkCompleted();
            }
        });
    }

    private void PauseActivePreviewRows()
    {
        RunOnDispatcher(() =>
        {
            foreach (var row in _previewRowsByItemKey.Values)
            {
                if (row.ProgressStateText == "Transferring")
                {
                    row.MarkPaused();
                }
            }
        });
    }

    private void SetBusyOperation(CancellationTokenSource cancellationTokenSource, BusyOperationKind busyOperationKind)
    {
        _busyOperationCancellationTokenSource = cancellationTokenSource;
        _busyOperationKind = busyOperationKind;
        RaisePropertyChanged(nameof(IsBusyOverlayVisible));
        (BusyOverlayTitle, BusyOverlayDescription) = busyOperationKind switch
        {
            BusyOperationKind.Analyze => ("Loading preview...", "Building the synchronization preview."),
            BusyOperationKind.Sync => ("Synchronizing...", "Applying the selected file operations."),
            _ => ("Working...", "Please wait."),
        };
        CancelBusyOperationCommand.RaiseCanExecuteChanged();
    }

    private void ClearBusyOperation(CancellationTokenSource cancellationTokenSource)
    {
        if (!ReferenceEquals(_busyOperationCancellationTokenSource, cancellationTokenSource))
        {
            return;
        }

        _busyOperationCancellationTokenSource = null;
        _busyOperationKind = BusyOperationKind.None;
        RaisePropertyChanged(nameof(IsBusyOverlayVisible));
        BusyOverlayTitle = "Working...";
        BusyOverlayDescription = "Please wait.";
        CancelBusyOperationCommand.RaiseCanExecuteChanged();
    }

    private void AddLog(string state, string message, SyncLogSeverity severity = SyncLogSeverity.Verbose)
    {
        RunOnDispatcher(() =>
        {
            ActivityLog.Insert(0, new SyncLogEntryViewModel(state, message, severity));
            const int maxLogEntries = 200;
            while (ActivityLog.Count > maxLogEntries)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }

            RefreshActivityLogView();
        });
    }

    private void RunOnDispatcher(Action action)
    {
        if (!_hasWpfApplication)
        {
            action();
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
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
        RefreshActivityLogView();
    }

    private void RefreshActivityLogView()
    {
        if (ActivityLogView is not DispatcherObject dispatcherObject || dispatcherObject.Dispatcher.CheckAccess())
        {
            ActivityLogView.Refresh();
        }
        else
        {
            dispatcherObject.Dispatcher.BeginInvoke(ActivityLogView.Refresh);
        }
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
