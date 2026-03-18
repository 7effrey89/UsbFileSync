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

public enum WorkspaceMode
{
    Sync,
    DriveTools,
}

public enum DriveToolKind
{
    Duplicates,
    ImageRename,
}

internal enum BusyOperationKind
{
    None,
    Analyze,
    FindDuplicates,
    Sync,
}

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromMilliseconds(250);
    private const double AnalyzeEtaSmoothingFactor = 0.25;
    private const double AnalyzeEtaMinimumProgressPercent = 10;

    private sealed record AnalyzeTimingHistory(TimeSpan AnalyzeDuration, TimeSpan PreviewDuration)
    {
        public TimeSpan TotalDuration => AnalyzeDuration + PreviewDuration;
    }

    private readonly SyncService _syncService;
    private readonly DuplicateFileAnalysisService _duplicateFileAnalysisService;
    private readonly ISyncSettingsStore? _settingsStore;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IUserDialogService _userDialogService;
    private readonly ISourceVolumeService _sourceVolumeService;
    private readonly ISourceVolumeService _destinationVolumeService;
    private readonly ISyncExecutionClient _syncExecutionClient;
    private readonly IFileLauncherService _fileLauncherService;
    private readonly IDriveDisplayNameService _driveDisplayNameService;
    private readonly Dispatcher _dispatcher;
    private readonly bool _hasWpfApplication;
    private readonly object _activityLogLock = new();
    private readonly Stopwatch _busyOperationStopwatch = new();
    private readonly Dictionary<string, AnalyzeTimingHistory> _analyzeTimingHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyncPreviewRowViewModel> _previewRowsByItemKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PreviewTabKind, ICollectionView> _previewViews = new();
    private readonly Dictionary<PreviewColumnKey, HashSet<string>> _activePreviewFilters = new();
    private readonly Dictionary<string, DuplicateFileEntry> _driveToolDuplicateEntriesByItemKey = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _persistConfigurationCancellationTokenSource;
    private CancellationTokenSource? _busyOperationCancellationTokenSource;
    private CancellationTokenSource? _syncCancellationTokenSource;
    private IReadOnlyList<SyncAction> _queuedActions = [];
    private int _completedQueuedActions;
    private string _currentTransferItem = "No active transfer.";
    private string _currentTransferDetails = "Queue is idle.";
    private double _currentTransferProgressValue;
    private double _busyOverlayProgressValue;
    private double? _busyOverlaySmoothedEtaSeconds;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private string _driveToolsPath = string.Empty;
    private SyncMode _selectedMode = SyncMode.OneWay;
    private WorkspaceMode _selectedWorkspaceMode;
    private DriveToolKind _selectedDriveTool = DriveToolKind.Duplicates;
    private int _parallelCopyCount = 1;
    private bool _detectMoves = true;
    private bool _dryRun = true;
    private bool _verifyChecksums;
    private bool _moveMode;
    private bool _includeSubfolders = true;
    private bool _driveToolsIncludeSubfolders = true;
    private bool _preventDeletingAllFilesInDuplicateGroup = true;
    private bool _hideMacOsSystemFiles = true;
    private IReadOnlyList<string> _excludedPathPatterns = Array.Empty<string>();
    private bool _useCustomCloudProviderCredentials;
    private Dictionary<string, string> _previewProviderMappings = PreviewProviderDefaults.CreateSerializableMapping();
    private IReadOnlyList<CloudProviderAppRegistration> _cloudProviderAppRegistrations = Array.Empty<CloudProviderAppRegistration>();
    private bool _isBusy;
    private bool _isSyncRunning;
    private bool _isLoadingSavedConfiguration;
    private double _progressValue;
    private string _statusMessage = "Configure the source and destination drives to begin.";
    private bool _isStatusSuccess;
    private bool _isSourcePathFocused;
    private bool _isDestinationPathFocused;
    private bool _isDriveToolsPathFocused;
    private BusyOperationKind _busyOperationKind;
    private string _busyOverlayTitle = "Working...";
    private string _busyOverlayDescription = "Please wait.";
    private string _busyOverlayCountersText = string.Empty;
    private string _busyOverlayPathText = string.Empty;
    private string _busyOverlayEtaText = string.Empty;
    private bool _isBusyOverlayProgressIndeterminate;
    private bool _isBusyOverlayDismissed;
    private volatile bool _useCompletedAnalyzeResultsRequested;
    private int _currentAnalyzeCompletedDestinationCount;
    private int _currentAnalyzeTotalDestinationCount;
    private PreviewColumnKey? _activePreviewFilterColumn;
    private PreviewColumnKey? _activePreviewSortColumn;
    private ListSortDirection _activePreviewSortDirection = ListSortDirection.Ascending;
    private string _previewFilterTitle = "Column filter";
    private string _previewFilterSearchText = string.Empty;
    private ActivityLogFilter _selectedActivityLogFilter = ActivityLogFilter.All;
    private bool _suppressSelectionUpdates;
    private bool _hasDriveToolDuplicateSelectionConflict;
    private HashSet<string> _conflictingDriveToolDuplicateGroupKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel()
        : this(
            new SyncService(),
            settingsStore: CreateDefaultSettingsStore(),
            folderPickerService: new WindowsFolderPickerService(),
            userDialogService: new WindowsUserDialogService(),
            fileLauncherService: new WindowsFileLauncherService(),
            sourceVolumeService: SyncVolumeServiceFactory.CreateSourceVolumeService(),
            destinationVolumeService: SyncVolumeServiceFactory.CreateDestinationVolumeService(),
            syncExecutionClient: new WorkerSyncExecutionClient())
    {
    }

    public MainWindowViewModel(
        SyncService syncService,
        DuplicateFileAnalysisService? duplicateFileAnalysisService = null,
        ISyncSettingsStore? settingsStore = null,
        IFolderPickerService? folderPickerService = null,
        IUserDialogService? userDialogService = null,
        IFileLauncherService? fileLauncherService = null,
        IDriveDisplayNameService? driveDisplayNameService = null,
        ISourceVolumeService? sourceVolumeService = null,
        ISourceVolumeService? destinationVolumeService = null,
        ISyncExecutionClient? syncExecutionClient = null)
    {
        _syncService = syncService;
        _duplicateFileAnalysisService = duplicateFileAnalysisService ?? new DuplicateFileAnalysisService();
        _settingsStore = settingsStore;
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _userDialogService = userDialogService ?? new WindowsUserDialogService();
        _sourceVolumeService = sourceVolumeService ?? SyncVolumeServiceFactory.CreateSourceVolumeService();
        _destinationVolumeService = destinationVolumeService ?? SyncVolumeServiceFactory.CreateDestinationVolumeService();
        _syncExecutionClient = syncExecutionClient ?? new InProcessSyncExecutionClient(syncService);
        _fileLauncherService = fileLauncherService ?? new WindowsFileLauncherService();
        _driveDisplayNameService = driveDisplayNameService ?? new WindowsDriveDisplayNameService(() => _cloudProviderAppRegistrations);
        _dispatcher = Dispatcher.CurrentDispatcher;
        _hasWpfApplication = System.Windows.Application.Current is not null;
        AvailableModes = Enum.GetValues<SyncMode>();
        PlannedActions = new BulkObservableCollection<SyncAction>();
        AdditionalDestinationPaths = new ObservableCollection<DestinationPathEntryViewModel>();
        NewFiles = new BulkObservableCollection<SyncPreviewRowViewModel>();
        ChangedFiles = new BulkObservableCollection<SyncPreviewRowViewModel>();
        DeletedFiles = new BulkObservableCollection<SyncPreviewRowViewModel>();
        UnchangedFiles = new BulkObservableCollection<SyncPreviewRowViewModel>();
        AllFiles = new BulkObservableCollection<SyncPreviewRowViewModel>();
        DriveToolDuplicateRows = new BulkObservableCollection<DriveToolDuplicateRowViewModel>();
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
        RemainingQueue = new BulkObservableCollection<QueueActionViewModel>();
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanExecuteSyncCommands);
        FindDuplicatesCommand = new AsyncRelayCommand(FindDuplicatesAsync, CanExecuteDuplicateAnalyzeCommand);
        DeleteSelectedDuplicatesCommand = new AsyncRelayCommand(DeleteSelectedDuplicatesAsync, CanDeleteSelectedDuplicates);
        ToggleSyncCommand = new RelayCommand(ToggleSync, CanExecuteToggleSyncCommand);
        CancelBusyOperationCommand = new RelayCommand(CancelBusyOperation, CanCancelBusyOperation);
        UseCompletedAnalyzeResultsCommand = new RelayCommand(UseCompletedAnalyzeResults, CanUseCompletedAnalyzeResults);
        BrowseSourcePathCommand = new RelayCommand(BrowseSourcePath);
        BrowseDriveToolsPathCommand = new RelayCommand(BrowseDriveToolsPath);
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

    public ObservableCollection<DriveToolDuplicateRowViewModel> DriveToolDuplicateRows { get; }

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

    public AsyncRelayCommand FindDuplicatesCommand { get; }

    public AsyncRelayCommand DeleteSelectedDuplicatesCommand { get; }

    public RelayCommand ToggleSyncCommand { get; }

    public RelayCommand CancelBusyOperationCommand { get; }

    public RelayCommand UseCompletedAnalyzeResultsCommand { get; }

    public RelayCommand BrowseSourcePathCommand { get; }

    public RelayCommand BrowseDriveToolsPathCommand { get; }

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

    public string DriveToolsPath
    {
        get => _driveToolsPath;
        set
        {
            if (SetProperty(ref _driveToolsPath, value))
            {
                RaisePropertyChanged(nameof(DriveToolsPathDisplayText));
                HandleDriveToolsConfigurationChanged();
            }
        }
    }

    public string SourcePathDisplayText => _isSourcePathFocused
        ? SourcePath
        : _driveDisplayNameService.FormatPathForDisplay(SourcePath);

    public string DestinationPathDisplayText => _isDestinationPathFocused
        ? DestinationPath
        : _driveDisplayNameService.FormatPathForDisplay(DestinationPath);

    public string DriveToolsPathDisplayText => _isDriveToolsPathFocused
        ? DriveToolsPath
        : _driveDisplayNameService.FormatPathForDisplay(DriveToolsPath);

    public bool HasAdditionalDestinationPaths => AdditionalDestinationPaths.Count > 0;

    public WorkspaceMode SelectedWorkspaceMode
    {
        get => _selectedWorkspaceMode;
        set
        {
            if (SetProperty(ref _selectedWorkspaceMode, value))
            {
                RaisePropertyChanged(nameof(IsSyncWorkspaceSelected));
                RaisePropertyChanged(nameof(IsDriveToolsWorkspaceSelected));
                RaisePropertyChanged(nameof(IsSyncWorkspaceVisible));
                RaisePropertyChanged(nameof(IsDriveToolsWorkspaceVisible));
                HandleDriveToolsConfigurationChanged();
            }
        }
    }

    public bool IsSyncWorkspaceSelected
    {
        get => SelectedWorkspaceMode == WorkspaceMode.Sync;
        set
        {
            if (value)
            {
                SelectedWorkspaceMode = WorkspaceMode.Sync;
            }
        }
    }

    public bool IsDriveToolsWorkspaceSelected
    {
        get => SelectedWorkspaceMode == WorkspaceMode.DriveTools;
        set
        {
            if (value)
            {
                SelectedWorkspaceMode = WorkspaceMode.DriveTools;
            }
        }
    }

    public bool IsSyncWorkspaceVisible => SelectedWorkspaceMode == WorkspaceMode.Sync;

    public bool IsDriveToolsWorkspaceVisible => SelectedWorkspaceMode == WorkspaceMode.DriveTools;

    public DriveToolKind SelectedDriveTool
    {
        get => _selectedDriveTool;
        set
        {
            if (SetProperty(ref _selectedDriveTool, value))
            {
                RaisePropertyChanged(nameof(IsDuplicateDriveToolSelected));
                RaisePropertyChanged(nameof(IsImageRenameDriveToolSelected));
                RaisePropertyChanged(nameof(IsDuplicateDriveToolVisible));
                RaisePropertyChanged(nameof(IsImageRenameDriveToolVisible));
                HandleDriveToolsConfigurationChanged();
            }
        }
    }

    public bool IsDuplicateDriveToolSelected
    {
        get => SelectedDriveTool == DriveToolKind.Duplicates;
        set
        {
            if (value)
            {
                SelectedDriveTool = DriveToolKind.Duplicates;
            }
        }
    }

    public bool IsImageRenameDriveToolSelected
    {
        get => SelectedDriveTool == DriveToolKind.ImageRename;
        set
        {
            if (value)
            {
                SelectedDriveTool = DriveToolKind.ImageRename;
            }
        }
    }

    public bool IsDuplicateDriveToolVisible => SelectedDriveTool == DriveToolKind.Duplicates;

    public bool IsImageRenameDriveToolVisible => SelectedDriveTool == DriveToolKind.ImageRename;

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

    public string DriveToolsLocationDescription => "Drive Tools works against one drive or folder at a time for duplicate cleanup and future image rename workflows.";

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

    public bool MoveMode
    {
        get => _moveMode;
        set
        {
            if (SetProperty(ref _moveMode, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set
        {
            if (SetProperty(ref _includeSubfolders, value))
            {
                HandleConfigurationChanged();
            }
        }
    }

    public bool DriveToolsIncludeSubfolders
    {
        get => _driveToolsIncludeSubfolders;
        set
        {
            if (SetProperty(ref _driveToolsIncludeSubfolders, value))
            {
                HandleDriveToolsConfigurationChanged();
            }
        }
    }

    public bool PreventDeletingAllFilesInDuplicateGroup
    {
        get => _preventDeletingAllFilesInDuplicateGroup;
        set
        {
            if (SetProperty(ref _preventDeletingAllFilesInDuplicateGroup, value))
            {
                RefreshDriveToolDuplicateSelectionSafety(showDialog: false);
                RaisePropertyChanged(nameof(IsDriveToolDuplicateDeletionWarningVisible));
                RaisePropertyChanged(nameof(DriveToolDuplicateDeletionWarningText));
                DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();

                if (!_isLoadingSavedConfiguration)
                {
                    ScheduleConfigurationPersist();
                }
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

    public IReadOnlyList<string> GetExcludedPathPatterns() => _excludedPathPatterns.ToList();

    public bool UseCustomCloudProviderCredentials
    {
        get => _useCustomCloudProviderCredentials;
        set
        {
            if (SetProperty(ref _useCustomCloudProviderCredentials, value))
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
                FindDuplicatesCommand.RaiseCanExecuteChanged();
                DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
                ToggleSyncCommand.RaiseCanExecuteChanged();
                CancelBusyOperationCommand.RaiseCanExecuteChanged();
                UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusyOverlayVisible =>
        IsBusy &&
        (_busyOperationKind == BusyOperationKind.Analyze || _busyOperationKind == BusyOperationKind.FindDuplicates) &&
        !_isBusyOverlayDismissed;

    public bool IsBusyOverlayProgressIndeterminate
    {
        get => _isBusyOverlayProgressIndeterminate;
        private set => SetProperty(ref _isBusyOverlayProgressIndeterminate, value);
    }

    public double BusyOverlayProgressValue
    {
        get => _busyOverlayProgressValue;
        private set => SetProperty(ref _busyOverlayProgressValue, value);
    }

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

    public string BusyOverlayCountersText
    {
        get => _busyOverlayCountersText;
        private set => SetProperty(ref _busyOverlayCountersText, value);
    }

    public string BusyOverlayPathText
    {
        get => _busyOverlayPathText;
        private set => SetProperty(ref _busyOverlayPathText, value);
    }

    public string BusyOverlayEtaText
    {
        get => _busyOverlayEtaText;
        private set => SetProperty(ref _busyOverlayEtaText, value);
    }
    
    public bool IsUseCompletedAnalyzeResultsVisible =>
        IsBusy &&
        _busyOperationKind == BusyOperationKind.Analyze &&
        _currentAnalyzeTotalDestinationCount > 1 &&
        _currentAnalyzeCompletedDestinationCount < _currentAnalyzeTotalDestinationCount;

    public bool IsSyncRunning
    {
        get => _isSyncRunning;
        private set
        {
            if (SetProperty(ref _isSyncRunning, value))
            {
                RaisePropertyChanged(nameof(SyncButtonText));
                AnalyzeCommand.RaiseCanExecuteChanged();
                FindDuplicatesCommand.RaiseCanExecuteChanged();
                DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
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

    public int DriveToolDuplicateRowCount => DriveToolDuplicateRows.Count;

    public int DriveToolDuplicateGroupCount => DriveToolDuplicateRows.Count(row => row.IsGroupHeader);

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

    public bool? AreAllDriveToolDuplicatesSelected
    {
        get => GetDriveToolDuplicateSelectionState();
        set => SetDriveToolDuplicateSelection(value);
    }

    public bool? AreAllFilesSelected
    {
        get => GetSelectionState(PreviewTabKind.AllFiles);
        set => SetSelection(PreviewTabKind.AllFiles, value);
    }

    public string QueueSummary => RemainingQueue.Count == 0
        ? "Queue empty"
        : $"{RemainingQueue.Count} queued item(s) remaining";

    public bool HasDriveToolDuplicateSelectionConflict
    {
        get => _hasDriveToolDuplicateSelectionConflict;
        private set
        {
            if (SetProperty(ref _hasDriveToolDuplicateSelectionConflict, value))
            {
                RaisePropertyChanged(nameof(IsDriveToolDuplicateDeletionWarningVisible));
                RaisePropertyChanged(nameof(DriveToolDuplicateDeletionWarningText));
                DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDriveToolDuplicateDeletionWarningVisible =>
        PreventDeletingAllFilesInDuplicateGroup && HasDriveToolDuplicateSelectionConflict;

    public string DriveToolDuplicateDeletionWarningText => IsDriveToolDuplicateDeletionWarningVisible
        ? "Deletion is blocked because every file in at least one duplicate group is selected. Leave one file unchecked in each highlighted group, or turn off the safety checkbox."
        : string.Empty;

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
        var selectedPath = WindowsSourceLocationPickerService.PickSourceLocation(SourcePath, _folderPickerService, GetCurrentSourceVolumeService());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SourcePath = selectedPath;
        }
    }

    private void BrowseDriveToolsPath()
    {
        var selectedPath = WindowsSourceLocationPickerService.PickSourceLocation(DriveToolsPath, _folderPickerService, GetCurrentSourceVolumeService());
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DriveToolsPath = selectedPath;
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
        var selectedPath = WindowsSourceLocationPickerService.PickDestinationLocation(destinationPath, _folderPickerService, GetCurrentDestinationBrowseVolumeService());
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
        if (TryGetOpenPath(parameter, out var path))
        {
            _fileLauncherService.OpenItem(path);
        }
    }

    private void OpenPreviewContainingFolder(object? parameter)
    {
        if (TryGetOpenPath(parameter, out var path))
        {
            _fileLauncherService.OpenContainingFolder(path);
        }
    }

    private void OpenPreviewFile(object? parameter)
    {
        if (TryGetExistingOpenPath(parameter, out var path))
        {
            _fileLauncherService.OpenFile(path);
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

    private static bool CanOpenPreviewItem(object? parameter) => parameter switch
    {
        SyncPreviewRowViewModel row => !string.IsNullOrWhiteSpace(row.OpenPath),
        DriveToolDuplicateRowViewModel row => row.HasOpenPath,
        _ => false,
    };

    private static bool CanOpenPreviewFile(object? parameter) => parameter switch
    {
        SyncPreviewRowViewModel row => File.Exists(row.OpenPath),
        DriveToolDuplicateRowViewModel row => row.HasOpenPath && File.Exists(row.OpenPath),
        _ => false,
    };

    private static bool CanOpenDestinationPreviewItem(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && !string.IsNullOrWhiteSpace(row.OpenDestinationPath);

    private static bool CanOpenDestinationPreviewFile(object? parameter) =>
        parameter is SyncPreviewRowViewModel row && File.Exists(row.OpenDestinationPath);

    private bool CanExecuteSyncCommands() => !IsBusy && !IsSyncRunning && IsConfigurationComplete();

    private bool CanExecuteDuplicateAnalyzeCommand() =>
        !IsBusy &&
        !IsSyncRunning &&
        SelectedWorkspaceMode == WorkspaceMode.DriveTools &&
        SelectedDriveTool == DriveToolKind.Duplicates &&
        !string.IsNullOrWhiteSpace(DriveToolsPath);

    private bool CanExecuteToggleSyncCommand() => IsSyncRunning || (!IsBusy && IsConfigurationComplete());

    private bool CanCancelBusyOperation() => _busyOperationCancellationTokenSource is { IsCancellationRequested: false };

    private bool CanDeleteSelectedDuplicates() =>
        !IsBusy &&
        !IsSyncRunning &&
        SelectedWorkspaceMode == WorkspaceMode.DriveTools &&
        SelectedDriveTool == DriveToolKind.Duplicates &&
        (!PreventDeletingAllFilesInDuplicateGroup || !HasDriveToolDuplicateSelectionConflict) &&
        DriveToolDuplicateRows.Any(row => row.CanSelect && row.IsSelected);

    internal bool CanModifyDriveToolDuplicate(object? parameter) =>
        !IsBusy &&
        !IsSyncRunning &&
        parameter is DriveToolDuplicateRowViewModel { HasOpenPath: true };

    private bool CanUseCompletedAnalyzeResults() =>
        IsBusy &&
        _busyOperationKind == BusyOperationKind.Analyze &&
        !_useCompletedAnalyzeResultsRequested &&
        _currentAnalyzeTotalDestinationCount > 1 &&
        _currentAnalyzeCompletedDestinationCount < _currentAnalyzeTotalDestinationCount;

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
                _isBusyOverlayDismissed = true;
                RaisePropertyChanged(nameof(IsBusyOverlayVisible));
                StatusMessage = "Cancelling preview generation...";
                AddLog("Analyze", "Cancel requested while building the preview.", SyncLogSeverity.Warning);
                break;
            case BusyOperationKind.FindDuplicates:
                _isBusyOverlayDismissed = true;
                RaisePropertyChanged(nameof(IsBusyOverlayVisible));
                StatusMessage = "Cancelling duplicate analysis...";
                AddLog("Duplicate Finder", "Cancel requested while hashing duplicate candidates.", SyncLogSeverity.Warning);
                break;
            case BusyOperationKind.Sync:
                StatusMessage = "Stopping synchronization...";
                AddLog("Sync", "Stop requested by user.");
                break;
        }

        CancelBusyOperationCommand.RaiseCanExecuteChanged();
    }

    private void UseCompletedAnalyzeResults()
    {
        if (!CanUseCompletedAnalyzeResults())
        {
            return;
        }

        _useCompletedAnalyzeResultsRequested = true;
        BusyOverlayDescription = "Finishing the current destination before using completed results.";
        AddLog("Analyze", "Use completed results requested. UsbFileSync will finish the current destination and then build a partial preview.", SyncLogSeverity.Warning);
        RaisePropertyChanged(nameof(IsUseCompletedAnalyzeResultsVisible));
        UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();
    }

    private SyncConfiguration CreateConfiguration(IReadOnlyList<string>? destinationPaths = null)
    {
        var sourceVolumeService = GetCurrentSourceVolumeService();
        var destinationVolumeService = GetCurrentDestinationVolumeService();
        var resolvedDestinationPaths = destinationPaths ?? GetDestinationPaths();
        return SyncVolumeServiceFactory.ResolveConfiguration(
            new SyncConfiguration
            {
                SourcePath = SourcePath,
                DestinationPath = resolvedDestinationPaths.FirstOrDefault() ?? string.Empty,
                DestinationPaths = resolvedDestinationPaths.ToList(),
                Mode = SelectedMode,
                DetectMoves = DetectMoves,
                DryRun = DryRun,
                VerifyChecksums = VerifyChecksums,
                MoveMode = MoveMode,
                IncludeSubfolders = IncludeSubfolders,
                PreventDeletingAllFilesInDuplicateGroup = PreventDeletingAllFilesInDuplicateGroup,
                HideMacOsSystemFiles = HideMacOsSystemFiles,
                ExcludedPathPatterns = _excludedPathPatterns.ToList(),
                ParallelCopyCount = ParallelCopyCount,
                PreviewProviderMappings = new Dictionary<string, string>(_previewProviderMappings, StringComparer.OrdinalIgnoreCase),
                UseCustomCloudProviderCredentials = UseCustomCloudProviderCredentials,
                CloudProviderAppRegistrations = _cloudProviderAppRegistrations.ToList(),
            },
            sourceVolumeService,
            destinationVolumeService);
    }

    private ISourceVolumeService GetCurrentSourceVolumeService() =>
        UseCustomCloudProviderCredentials
            ? SyncVolumeServiceFactory.CreateSourceVolumeService(UseCustomCloudProviderCredentials, _cloudProviderAppRegistrations)
            : _sourceVolumeService;

    private ISourceVolumeService GetCurrentDestinationVolumeService() =>
        UseCustomCloudProviderCredentials
            ? SyncVolumeServiceFactory.CreateDestinationVolumeService(UseCustomCloudProviderCredentials, _cloudProviderAppRegistrations)
            : _destinationVolumeService;

    private ISourceVolumeService GetCurrentDestinationBrowseVolumeService() =>
        UseCustomCloudProviderCredentials
            ? SyncVolumeServiceFactory.CreateDestinationBrowseVolumeService(UseCustomCloudProviderCredentials, _cloudProviderAppRegistrations)
            : WindowsSourceLocationPickerService.GetDestinationBrowseVolumeService(_destinationVolumeService);

    public void UpdateParallelCopyCount(int parallelCopyCount)
    {
        ParallelCopyCount = parallelCopyCount;
        AddLog("Settings", ParallelCopyCount == 0
            ? "Parallel copy count set to auto."
            : $"Parallel copy count set to {ParallelCopyCount}.");
    }

    public IReadOnlyDictionary<string, string> GetPreviewProviderMappings() =>
        new Dictionary<string, string>(_previewProviderMappings, StringComparer.OrdinalIgnoreCase);

    internal string? BrowseForDriveToolMoveTarget(string? initialPath) =>
        _folderPickerService.PickFolder("Choose a destination folder for the duplicate file", initialPath);

    public void UpdateExcludedPathPatterns(IReadOnlyList<string> excludedPathPatterns)
    {
        _excludedPathPatterns = (excludedPathPatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        HandleConfigurationChanged();
        AddLog(
            "Settings",
            _excludedPathPatterns.Count == 0
                ? "Path exclusion patterns cleared."
                : $"Path exclusion patterns updated for {_excludedPathPatterns.Count} pattern(s).");
    }

    public IReadOnlyList<CloudProviderAppRegistration> GetCloudProviderAppRegistrations() =>
        _cloudProviderAppRegistrations.ToList();

    public bool GetUseCustomCloudProviderCredentials() => UseCustomCloudProviderCredentials;

    public void UpdatePreviewProviderMappings(IReadOnlyDictionary<string, string> mappings)
    {
        _previewProviderMappings = new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase);
        HandleConfigurationChanged();
        AddLog("Settings", $"Preview provider mappings updated for {_previewProviderMappings.Count} file extensions.");
    }

    public void UpdateCloudProviderAppRegistrations(IReadOnlyList<CloudProviderAppRegistration> registrations)
    {
        _cloudProviderAppRegistrations = (registrations ?? Array.Empty<CloudProviderAppRegistration>())
            .OrderBy(registration => registration.Provider)
            .ToList();
        HandleConfigurationChanged();
        AddLog(
            "Settings",
            _cloudProviderAppRegistrations.Count == 0
                ? "Cloud provider app registrations cleared."
                : $"Cloud provider app registrations updated for {_cloudProviderAppRegistrations.Count} provider(s).");
    }

    public void UpdateUseCustomCloudProviderCredentials(bool useCustomCloudProviderCredentials)
    {
        UseCustomCloudProviderCredentials = useCustomCloudProviderCredentials;
        AddLog(
            "Settings",
            UseCustomCloudProviderCredentials
                ? "Custom cloud provider credentials will be preferred over the built-in defaults."
                : "Built-in cloud provider credentials will be preferred unless custom credentials are enabled.");
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

    public void SelectAllDriveToolDuplicates()
    {
        UpdateDriveToolDuplicateSelection(_ => true);
    }

    public void InvertDriveToolDuplicateSelection()
    {
        UpdateDriveToolDuplicateSelection(row => !row.IsSelected);
    }

    public int SelectDriveToolDuplicatesByPattern(string keyword, PreviewSelectionTarget target)
    {
        var normalizedKeyword = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return 0;
        }

        var rows = GetSelectableDriveToolDuplicateRows();
        if (rows.Count == 0)
        {
            return 0;
        }

        UpdateDriveToolDuplicateSelection(row => MatchesDriveToolPattern(row, normalizedKeyword, target));
        return rows.Count(row => row.IsSelected);
    }

    public void OpenPreviewColumnFilter(PreviewTabKind tabKind, PreviewColumnHeader header)
    {
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

    public void SetDriveToolsPathFocused(bool isFocused)
    {
        if (SetProperty(ref _isDriveToolsPathFocused, isFocused))
        {
            RaisePropertyChanged(nameof(DriveToolsPathDisplayText));
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
        ReplaceDriveToolDuplicateEntries([]);
        ReplaceDriveToolDuplicateRows([]);

        if (!TryValidateConfiguration(requireAccessibleDestinationPath: false, out var destinationPathsToAnalyze, out var skippedDestinationPaths))
        {
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        LogSkippedDestinationPaths(skippedDestinationPaths, "Analyze");

        if (destinationPathsToAnalyze.Count == 0)
        {
            ReplaceActions([]);
            ReplacePreviewRows([]);
            ReplaceQueue([]);
            BusyOverlayProgressValue = 0;
            CurrentTransferProgressValue = 0;
            CurrentTransferItem = "No file operations required.";
            CurrentTransferDetails = QueueSummary;
            StatusMessage = "Analysis skipped because all destination paths match the source path.";
            AddLog("Analyze", StatusMessage, SyncLogSeverity.Warning);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.Analyze);
        BusyOverlayProgressValue = 0;
        _currentAnalyzeCompletedDestinationCount = 0;
        _currentAnalyzeTotalDestinationCount = destinationPathsToAnalyze.Count;
        RaisePropertyChanged(nameof(IsUseCompletedAnalyzeResultsVisible));
        UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();

        await RunBusyOperationAsync(async () =>
        {
            await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);

            var analysisTask = Task.Run(async () =>
            {
                var totalSteps = destinationPathsToAnalyze.Count * 2;
                var completedSteps = 0;
                var allActions = new List<SyncAction>();
                var allPreviewItems = new List<SyncPreviewItem>();

                for (var index = 0; index < destinationPathsToAnalyze.Count; index++)
                {
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var destinationPath = destinationPathsToAnalyze[index];
                    var destinationLabel = FormatPathForProgress(destinationPath);
                    var configuration = CreateConfiguration([destinationPath]);
                    var historyKey = CreateAnalyzeTimingHistoryKey(configuration);
                    _analyzeTimingHistory.TryGetValue(historyKey, out var analyzeTimingHistory);
                    var historicalDurationAfterCurrentDestination = GetHistoricalRemainingAnalyzeDuration(destinationPathsToAnalyze, index);
                    var analyzeStopwatch = Stopwatch.StartNew();

                    ReportAnalyzeProgress(destinationLabel, "Analyzing changes", completedSteps, totalSteps);
                    var destinationActions = await _syncService.AnalyzeChangesAsync(
                        configuration,
                        cancellationTokenSource.Token,
                        progress: new Progress<AnalyzeProgress>(progress =>
                            OnAnalyzeProgress(
                                progress,
                                completedSteps,
                                totalSteps,
                                analyzeStopwatch.Elapsed,
                                analyzeTimingHistory,
                                historicalDurationAfterCurrentDestination))).ConfigureAwait(false);
                    var analyzeDuration = analyzeStopwatch.Elapsed;
                    allActions.AddRange(destinationActions);
                    completedSteps++;
                    ReportAnalyzeProgress(destinationLabel, "Building preview", completedSteps, totalSteps);

                    var previewStopwatch = Stopwatch.StartNew();
                    var destinationPreview = _syncService.BuildPreview(configuration, destinationActions, cancellationTokenSource.Token);
                    var previewDuration = previewStopwatch.Elapsed;
                    _analyzeTimingHistory[historyKey] = new AnalyzeTimingHistory(analyzeDuration, previewDuration);
                    allPreviewItems.AddRange(destinationPreview);
                    completedSteps++;
                    _currentAnalyzeCompletedDestinationCount = index + 1;
                    RunOnDispatcher(() =>
                    {
                        RaisePropertyChanged(nameof(IsUseCompletedAnalyzeResultsVisible));
                        UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();
                    });
                    ReportAnalyzeProgress(destinationLabel, "Completed", completedSteps, totalSteps);

                    if (_useCompletedAnalyzeResultsRequested)
                    {
                        break;
                    }
                }

                return new AnalyzePreviewResult(
                    allActions,
                    allPreviewItems,
                    _useCompletedAnalyzeResultsRequested && _currentAnalyzeCompletedDestinationCount < destinationPathsToAnalyze.Count,
                    _currentAnalyzeCompletedDestinationCount,
                    destinationPathsToAnalyze.Count);
            }, CancellationToken.None);

            var completedTask = await Task.WhenAny(
                analysisTask,
                Task.Delay(Timeout.Infinite, cancellationTokenSource.Token)).ConfigureAwait(true);

            if (!ReferenceEquals(completedTask, analysisTask))
            {
                ObserveBackgroundAnalyzeTask(analysisTask);
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
            }

            var analysisResult = await analysisTask.ConfigureAwait(true);
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            await ApplyAnalyzePreviewResultAsync(analysisResult, cancellationTokenSource.Token).ConfigureAwait(true);
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        ClearBusyOperation(cancellationTokenSource);
        cancellationTokenSource.Dispose();
    }

    private async Task FindDuplicatesAsync()
    {
        if (!TryResolveSingleVolume(DriveToolsPath, "Drive Tools", out var sourceVolume, out var validationMessage))
        {
            StatusMessage = validationMessage;
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.FindDuplicates);
        BusyOverlayProgressValue = 0;

        await RunBusyOperationAsync(async () =>
        {
            await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);

            var analysisResult = await Task.Run(async () =>
                await _duplicateFileAnalysisService.AnalyzeAsync(
                    sourceVolume!,
                    HideMacOsSystemFiles,
                    _excludedPathPatterns,
                    DriveToolsIncludeSubfolders,
                    cancellationTokenSource.Token,
                    progress: new Progress<DuplicateAnalyzeProgress>(progress =>
                    {
                        RunOnDispatcher(() =>
                        {
                            IsBusyOverlayProgressIndeterminate = false;
                            BusyOverlayProgressValue = progress.TotalFilesToHash == 0
                                ? 100
                                : Math.Clamp((progress.HashedFiles * 100d) / progress.TotalFilesToHash, 0d, 100d);
                            BusyOverlayDescription = progress.TotalFilesToHash == 0
                                ? "No checksum candidates required hashing."
                                : $"Hashing {progress.HashedFiles:N0} of {progress.TotalFilesToHash:N0} duplicate candidates...";
                            BusyOverlayCountersText = progress.DuplicateGroupsFound == 0
                                ? "No identical checksum groups confirmed yet."
                                : $"{progress.DuplicateGroupsFound:N0} duplicate group(s) confirmed.";
                            BusyOverlayPathText = FormatPathForProgress(progress.CurrentPath);
                            BusyOverlayEtaText = string.Empty;
                        });
                    })).ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(true);

            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            ApplyDuplicateAnalysisResult(analysisResult);
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        ClearBusyOperation(cancellationTokenSource);
        cancellationTokenSource.Dispose();
    }

    private async Task DeleteSelectedDuplicatesAsync()
    {
        RefreshDriveToolDuplicateSelectionSafety(showDialog: false);

        var selectedCandidates = DriveToolDuplicateRows
            .Where(row => row.CanSelect && row.IsSelected)
            .Select(row => row.FileEntry)
            .OfType<DuplicateFileEntry>()
            .ToList();

        if (selectedCandidates.Count == 0)
        {
            StatusMessage = "Select at least one duplicate file to delete.";
            return;
        }

        if (PreventDeletingAllFilesInDuplicateGroup && HasDriveToolDuplicateSelectionConflict)
        {
            StatusMessage = "Leave at least one file unchecked in each red-marked checksum group before deleting.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return;
        }

        if (!TryResolveSingleVolume(DriveToolsPath, "Drive Tools", out var sourceVolume, out var validationMessage))
        {
            StatusMessage = validationMessage;
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        if (sourceVolume!.IsReadOnly)
        {
            StatusMessage = "The selected source location is read-only, so duplicate files cannot be deleted.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.Sync);

        await RunBusyOperationAsync(async () =>
        {
            var deletedCount = await Task.Run(() =>
                _duplicateFileAnalysisService.DeleteDuplicates(sourceVolume, selectedCandidates, cancellationTokenSource.Token),
                cancellationTokenSource.Token).ConfigureAwait(true);

            var deletedKeys = selectedCandidates
                .Select(candidate => candidate.ItemKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ReplaceDriveToolDuplicateEntries(_driveToolDuplicateEntriesByItemKey.Values.Where(candidate => !deletedKeys.Contains(candidate.ItemKey)));
            ReplaceDriveToolDuplicateRows(DriveToolDuplicateRows.Where(row => row.IsGroupHeader || !deletedKeys.Contains(row.ItemKey)).ToList());
            RemoveEmptyDriveToolDuplicateGroups();
            SetStatusMessage(
                deletedCount == 1
                    ? "Deleted 1 duplicate file."
                    : $"Deleted {deletedCount} duplicate files.",
                isSuccess: true);
            CurrentTransferItem = deletedCount == 1 ? "1 duplicate removed." : $"{deletedCount} duplicates removed.";
            CurrentTransferDetails = DriveToolDuplicateGroupCount == 0
                ? "No duplicate candidates remain."
                : $"{DriveToolDuplicateGroupCount} checksum group(s) remain.";
            CurrentTransferProgressValue = 0;
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Verbose);
        }, cancellationTokenSource.Token).ConfigureAwait(true);

        ClearBusyOperation(cancellationTokenSource);
        cancellationTokenSource.Dispose();
    }

    private async Task StartSyncAsync()
    {
        if (!TryValidateConfiguration(requireAccessibleDestinationPath: true, out var destinationPathsToSync, out var skippedDestinationPaths))
        {
            AddLog("Error", StatusMessage, SyncLogSeverity.Error);
            return;
        }

        LogSkippedDestinationPaths(skippedDestinationPaths, "Sync");

        if (destinationPathsToSync.Count == 0)
        {
            StatusMessage = "Synchronization skipped because all destination paths match the source path.";
            AddLog("Sync", StatusMessage, SyncLogSeverity.Warning);
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _syncCancellationTokenSource = cancellationTokenSource;
        SetBusyOperation(cancellationTokenSource, BusyOperationKind.Sync);
        IsSyncRunning = true;

        await RunBusyOperationAsync(async () =>
        {
            var configuration = CreateConfiguration(destinationPathsToSync);
            if (PlannedActions.Count == 0)
            {
                await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);

                var initialAnalysis = await Task.Run(async () =>
                {
                    var initialActions = await _syncService.AnalyzeChangesAsync(configuration, cancellationToken: cancellationTokenSource.Token).ConfigureAwait(false);
                    var initialPreview = _syncService.BuildPreview(configuration, initialActions, cancellationTokenSource.Token);
                    return new AnalyzePreviewResult(initialActions, initialPreview, false, 1, 1);
                }, cancellationTokenSource.Token).ConfigureAwait(true);

                var initialActions = initialAnalysis.Actions;
                var initialPreview = initialAnalysis.Preview;
                ReplaceActions(initialActions);
                await ReplacePreviewAsync(initialPreview, cancellationTokenSource.Token).ConfigureAwait(true);
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
            var verifiedCopyCount = (configuration.VerifyChecksums || configuration.MoveMode)
                ? actions.Count(action => action.Type is
                    SyncActionType.CopyToDestination or
                    SyncActionType.CopyToSource or
                    SyncActionType.OverwriteFileOnDestination or
                    SyncActionType.OverwriteFileOnSource)
                : 0;
            SetStatusMessage(
                result.IsDryRun
                    ? $"Dry run complete with {result.Actions.Count} planned action(s)."
                    : BuildCompletionMessage(result.AppliedOperations, configuration.VerifyChecksums || configuration.MoveMode, verifiedCopyCount),
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
        FindDuplicatesCommand.RaiseCanExecuteChanged();
        DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
        ToggleSyncCommand.RaiseCanExecuteChanged();

        if (_isLoadingSavedConfiguration)
        {
            return;
        }

        ScheduleConfigurationPersist();
    }

    private void HandleDriveToolsConfigurationChanged()
    {
        FindDuplicatesCommand.RaiseCanExecuteChanged();
        DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
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
        GetDestinationPaths().Count > 0;

    private bool TryValidateConfiguration(
        bool requireAccessibleDestinationPath,
        out IReadOnlyList<string> validDestinationPaths,
        out IReadOnlyList<string> skippedDestinationPaths)
    {
        var destinationPaths = GetDestinationPaths();
        validDestinationPaths = Array.Empty<string>();
        skippedDestinationPaths = Array.Empty<string>();
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

        skippedDestinationPaths = destinationPaths
            .Where(destinationPath => string.Equals(SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        validDestinationPaths = destinationPaths
            .Where(destinationPath => !string.Equals(SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!requireAccessibleDestinationPath)
        {
            return true;
        }

        for (var index = 0; index < validDestinationPaths.Count; index++)
        {
            var label = validDestinationPaths.Count == 1
                ? "Destination"
                : $"Destination {index + 1}";
            if (!TryValidateDestinationPath(validDestinationPaths[index], label, out var destinationValidationMessage))
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
            var sourceVolumeService = GetCurrentSourceVolumeService();
            if (sourceVolumeService.TryCreateVolume(path, out _, out var sourceFailureReason))
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage = string.IsNullOrWhiteSpace(sourceFailureReason)
                ? $"{label} path is invalid."
                : $"{label} volume could not be opened. {sourceFailureReason}";
            return false;
        }

        var configuredSourceVolumeService = GetCurrentSourceVolumeService();
        if (configuredSourceVolumeService.TryCreateVolume(path, out _, out var failureReason))
        {
            validationMessage = string.Empty;
            return true;
        }

        validationMessage = string.IsNullOrWhiteSpace(failureReason)
            ? $"{label} path does not exist."
            : $"{label} volume could not be opened. {failureReason}";
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

        var destinationVolumeService = GetCurrentDestinationVolumeService();
        if (destinationVolumeService.TryCreateVolume(path, out var volume, out var failureReason))
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
            : $"{label} volume could not be opened. {failureReason}";
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
            MoveMode = savedConfiguration.MoveMode;
            IncludeSubfolders = savedConfiguration.IncludeSubfolders;
            PreventDeletingAllFilesInDuplicateGroup = savedConfiguration.PreventDeletingAllFilesInDuplicateGroup;
            HideMacOsSystemFiles = savedConfiguration.HideMacOsSystemFiles;
            _excludedPathPatterns = (savedConfiguration.ExcludedPathPatterns ?? Array.Empty<string>())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            ParallelCopyCount = savedConfiguration.ParallelCopyCount;
            UseCustomCloudProviderCredentials = savedConfiguration.UseCustomCloudProviderCredentials;
            _previewProviderMappings = savedConfiguration.PreviewProviderMappings?.Count > 0
                ? new Dictionary<string, string>(savedConfiguration.PreviewProviderMappings, StringComparer.OrdinalIgnoreCase)
                : PreviewProviderDefaults.CreateSerializableMapping();
            _cloudProviderAppRegistrations = (savedConfiguration.CloudProviderAppRegistrations ?? Array.Empty<CloudProviderAppRegistration>())
                .OrderBy(registration => registration.Provider)
                .ToList();
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
                BusyOperationKind.FindDuplicates => "Duplicate analysis cancelled.",
                BusyOperationKind.Sync => "Synchronization stopped.",
                _ => "Operation cancelled.",
            };
            CurrentTransferItem = _busyOperationKind switch
            {
                BusyOperationKind.Analyze => "Preview generation cancelled.",
                BusyOperationKind.FindDuplicates => "Duplicate analysis cancelled.",
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
                _busyOperationKind switch
                {
                    BusyOperationKind.Analyze => "Preview generation cancelled.",
                    BusyOperationKind.FindDuplicates => "Duplicate analysis cancelled.",
                    _ => "Synchronization cancelled.",
                },
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

    private static void ObserveBackgroundAnalyzeTask(Task<AnalyzePreviewResult> analysisTask)
    {
        _ = analysisTask.ContinueWith(
            static task =>
            {
                _ = task.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record AnalyzePreviewResult(
        IReadOnlyList<SyncAction> Actions,
        IReadOnlyList<SyncPreviewItem> Preview,
        bool UsedCompletedResults,
        int CompletedDestinations,
        int TotalDestinations);

    private async Task ApplyAnalyzePreviewResultAsync(AnalyzePreviewResult analysisResult, CancellationToken cancellationToken)
    {
        var actions = analysisResult.Actions;
        var preview = analysisResult.Preview;

        ReplaceDriveToolDuplicateEntries([]);
        ReplaceDriveToolDuplicateRows([]);
        ReplaceActions(actions);
        await ReplacePreviewAsync(preview, cancellationToken).ConfigureAwait(true);
        ReplaceQueue(GetSelectedActions());

        var isPartial = analysisResult.UsedCompletedResults && analysisResult.CompletedDestinations < analysisResult.TotalDestinations;
        StatusMessage = BuildAnalyzeCompletionMessage(actions.Count, isPartial, analysisResult.CompletedDestinations, analysisResult.TotalDestinations);
        ProgressValue = 100;
        CurrentTransferProgressValue = 0;
        CurrentTransferItem = actions.Count == 0 ? "No file operations required." : isPartial ? "Partial preview ready." : "Preview ready.";
        CurrentTransferDetails = QueueSummary;
        AddLog("Analyze", StatusMessage, isPartial ? SyncLogSeverity.Warning : SyncLogSeverity.Verbose);
    }

    private void ApplyDuplicateAnalysisResult(DuplicateAnalysisResult analysisResult)
    {
        var duplicateRows = CreateDriveToolDuplicateRows(analysisResult.Groups);
        ReplaceDriveToolDuplicateEntries(analysisResult.Groups.SelectMany(group => group.Files));
        ReplaceDriveToolDuplicateRows(duplicateRows);
        ReplaceActions([]);
        ReplaceQueue([]);

        var duplicateCount = analysisResult.Groups.Sum(group => group.Files.Count);
        var message = duplicateCount == 0
            ? "No duplicated files were found in the selected source location."
            : $"Found {duplicateCount} duplicate file(s) across {analysisResult.DuplicateGroupCount} checksum-matched group(s). Leave one copy unchecked in each group, then select the copies you want UsbFileSync to delete.";
        SetStatusMessage(message, isSuccess: duplicateCount == 0);
        CurrentTransferItem = duplicateCount == 0 ? "Duplicate analysis complete." : "Duplicate results ready.";
        CurrentTransferDetails = duplicateCount == 0
            ? "No redundant files found."
            : $"{analysisResult.DuplicateGroupCount} duplicate group(s) confirmed with SHA-256.";
        CurrentTransferProgressValue = 0;
        AddLog("Duplicate Finder", StatusMessage, duplicateCount == 0 ? SyncLogSeverity.Verbose : SyncLogSeverity.Warning);
    }

    private IReadOnlyList<DriveToolDuplicateRowViewModel> CreateDriveToolDuplicateRows(IEnumerable<DuplicateFileGroup> groups)
    {
        var rows = new List<DriveToolDuplicateRowViewModel>();
        foreach (var group in groups
            .Where(group => group.Files.Count > 0)
            .OrderBy(group => group.Files[0].RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var firstFile = group.Files[0];
            rows.Add(new DriveToolDuplicateRowViewModel(
                itemKey: $"summary|{group.GroupKey}",
                groupKey: group.GroupKey,
                isGroupHeader: true,
                displayName: "Duplicate group",
                displayPath: $"{group.Files.Count} matching file(s)",
                checksumText: group.ChecksumSha256,
                fileType: Path.GetExtension(firstFile.FullPath).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension ? extension : "File",
                sizeText: FormatDriveToolSize(group.Length)));

            foreach (var file in group.Files)
            {
                rows.Add(new DriveToolDuplicateRowViewModel(
                    itemKey: file.ItemKey,
                    groupKey: group.GroupKey,
                    isGroupHeader: false,
                    displayName: Path.GetFileName(file.FullPath),
                    displayPath: _driveDisplayNameService.FormatDestinationPathForDisplay(file.FullPath),
                    checksumText: string.Empty,
                    fileType: string.Empty,
                    sizeText: string.Empty,
                    fileEntry: file));
            }
        }

        return rows;
    }

    private void ReplaceDriveToolDuplicateEntries(IEnumerable<DuplicateFileEntry> candidates)
    {
        _driveToolDuplicateEntriesByItemKey.Clear();
        foreach (var candidate in candidates)
        {
            _driveToolDuplicateEntriesByItemKey[candidate.ItemKey] = candidate;
        }

        DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
    }

    private void ReplaceDriveToolDuplicateRows(IReadOnlyList<DriveToolDuplicateRowViewModel> rows)
    {
        RunOnDispatcher(() =>
        {
            foreach (var existingRow in DriveToolDuplicateRows)
            {
                existingRow.PropertyChanged -= OnDriveToolDuplicateRowPropertyChanged;
            }

            ReplaceCollection(DriveToolDuplicateRows, rows);
            foreach (var row in DriveToolDuplicateRows)
            {
                row.PropertyChanged += OnDriveToolDuplicateRowPropertyChanged;
            }
            RaisePropertyChanged(nameof(DriveToolDuplicateRowCount));
            RaisePropertyChanged(nameof(DriveToolDuplicateGroupCount));
            RaisePropertyChanged(nameof(AreAllDriveToolDuplicatesSelected));
            RefreshDriveToolDuplicateSelectionSafety(showDialog: false);
            DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
        });
    }

    private void RemoveEmptyDriveToolDuplicateGroups()
    {
        var rowsToKeep = new List<DriveToolDuplicateRowViewModel>();
        DriveToolDuplicateRowViewModel? pendingHeader = null;
        var currentGroupChildren = new List<DriveToolDuplicateRowViewModel>();

        void FlushCurrentGroup()
        {
            if (pendingHeader is null || currentGroupChildren.Count < 2)
            {
                return;
            }

            rowsToKeep.Add(pendingHeader);
            rowsToKeep.AddRange(currentGroupChildren);
        }

        foreach (var row in DriveToolDuplicateRows)
        {
            if (row.IsGroupHeader)
            {
                FlushCurrentGroup();
                pendingHeader = row;
                currentGroupChildren.Clear();
                continue;
            }

            currentGroupChildren.Add(row);
        }

        FlushCurrentGroup();
        ReplaceDriveToolDuplicateRows(rowsToKeep);
    }

    private bool TryResolveSingleVolume(string path, string label, out IVolumeSource? sourceVolume, out string validationMessage)
    {
        sourceVolume = null;
        if (!TryValidateSyncPath(path, label, requireExistingDirectory: true, out validationMessage))
        {
            return false;
        }

        var sourceVolumeService = GetCurrentSourceVolumeService();
        if (sourceVolumeService.TryCreateVolume(path, out sourceVolume, out var failureReason) &&
            sourceVolume is not null)
        {
            validationMessage = string.Empty;
            return true;
        }

        try
        {
            sourceVolume = new WindowsMountedVolume(path);
            validationMessage = string.Empty;
            return true;
        }
        catch (Exception)
        {
            validationMessage = string.IsNullOrWhiteSpace(failureReason)
                ? $"{label} path could not be opened."
                : $"{label} volume could not be opened. {failureReason}";
            return false;
        }
    }

    private static string BuildAnalyzeCompletionMessage(int actionCount, bool isPartial, int completedDestinations, int totalDestinations)
    {
        if (!isPartial)
        {
            return actionCount == 0
                ? "Folders are already synchronized."
                : $"Preview generated with {actionCount} planned action(s). Select the items to synchronize.";
        }

        return actionCount == 0
            ? $"Preview generated from {completedDestinations} of {totalDestinations} destination(s) using completed results. No file operations were found in the completed results."
            : $"Preview generated from {completedDestinations} of {totalDestinations} destination(s) using completed results with {actionCount} planned action(s). Analyze again for a full refresh.";
    }

    private void ReportAnalyzeProgress(string destinationPath, string phase, int completedSteps, int totalSteps)
    {
        var normalizedTotalSteps = Math.Max(1, totalSteps);
        var progressValue = Math.Clamp((completedSteps * 100d) / normalizedTotalSteps, 0, 100);

        RunOnDispatcher(() =>
        {
            if (completedSteps > 0 && IsBusyOverlayProgressIndeterminate)
            {
                IsBusyOverlayProgressIndeterminate = false;
            }

            BusyOverlayProgressValue = progressValue;
            var description = totalSteps <= 2
                ? $"{phase} for {destinationPath}."
                : $"{phase} for {destinationPath} ({completedSteps}/{totalSteps} steps).";

            if (_useCompletedAnalyzeResultsRequested && _currentAnalyzeCompletedDestinationCount < _currentAnalyzeTotalDestinationCount)
            {
                description = $"{description} Finishing the current destination before using completed results.";
            }

            BusyOverlayDescription = description;
        });

        UpdateAnalyzeEta(completedSteps, totalSteps);
    }

    private void OnAnalyzeProgress(
        AnalyzeProgress progress,
        int completedSteps,
        int totalSteps,
        TimeSpan analyzeElapsed,
        AnalyzeTimingHistory? analyzeTimingHistory,
        TimeSpan historicalDurationAfterCurrentDestination)
    {
        var formattedRootPath = FormatPathForProgress(progress.RootPath);
        var formattedCurrentPath = FormatPathForProgress(progress.CurrentPath);
        var abbreviatedPath = AbbreviateAnalyzePath(formattedCurrentPath, formattedRootPath);

        var countersText = progress.DirectoriesScanned > 0
            ? $"{progress.FilesScanned:N0} files scanned | {progress.DirectoriesScanned:N0} folders scanned"
            : $"{progress.FilesScanned:N0} files scanned";

        if (analyzeElapsed.TotalSeconds >= 1 && progress.FilesScanned > 0)
        {
            var rate = (long)(progress.FilesScanned / analyzeElapsed.TotalSeconds);
            countersText += $" | ~{rate:N0} files/s";
        }

        BusyOverlayCountersText = countersText;
        BusyOverlayPathText = abbreviatedPath;

        // Estimate progress within the analyze step from the ratio of processed
        // to total directories.  totalDiscovered = root + all discovered subdirs.
        // processed = totalDiscovered − pendingDirectories.  This rises
        // monotonically from 0 → 1 and works on the very first scan without any
        // historical data, because the scanner reports its live queue depth.
        var totalDiscovered = 1 + progress.DirectoriesScanned;
        var processed = totalDiscovered - progress.PendingDirectories;
        if (totalDiscovered > 1 && processed > 0)
        {
            if (IsBusyOverlayProgressIndeterminate)
            {
                IsBusyOverlayProgressIndeterminate = false;
            }

            var normalizedTotalSteps = Math.Max(1, totalSteps);
            var stepWeight = 100d / normalizedTotalSteps;
            var withinStepFraction = Math.Clamp((double)processed / totalDiscovered, 0, 0.95);
            var interpolatedProgress = (completedSteps * stepWeight) + (stepWeight * withinStepFraction);
            BusyOverlayProgressValue = Math.Clamp(interpolatedProgress, 0, 99);
        }

        if (!TryUpdateAnalyzeEtaFromHistory(analyzeElapsed, analyzeTimingHistory, historicalDurationAfterCurrentDestination))
        {
            UpdateAnalyzeEta(completedSteps, totalSteps);
        }
    }

    private bool TryUpdateAnalyzeEtaFromHistory(
        TimeSpan analyzeElapsed,
        AnalyzeTimingHistory? analyzeTimingHistory,
        TimeSpan historicalDurationAfterCurrentDestination)
    {
        if (_busyOperationKind != BusyOperationKind.Analyze || analyzeTimingHistory is null)
        {
            return false;
        }

        if (analyzeTimingHistory.AnalyzeDuration <= TimeSpan.Zero)
        {
            return false;
        }

        var currentAnalyzeRemaining = analyzeTimingHistory.AnalyzeDuration - analyzeElapsed;
        if (currentAnalyzeRemaining < TimeSpan.Zero)
        {
            currentAnalyzeRemaining = TimeSpan.Zero;
        }

        var rawEtaSeconds = (currentAnalyzeRemaining + analyzeTimingHistory.PreviewDuration + historicalDurationAfterCurrentDestination).TotalSeconds;
        if (double.IsNaN(rawEtaSeconds) || double.IsInfinity(rawEtaSeconds) || rawEtaSeconds < 0)
        {
            return false;
        }

        SetAnalyzeEta(rawEtaSeconds);
        return true;
    }

    private void UpdateAnalyzeEta(int completedSteps, int totalSteps)
    {
        if (_busyOperationKind != BusyOperationKind.Analyze)
        {
            BusyOverlayEtaText = string.Empty;
            return;
        }

        if (BusyOverlayProgressValue >= 100 || completedSteps >= totalSteps)
        {
            BusyOverlayEtaText = string.Empty;
            return;
        }

        var minimumCompletedSteps = totalSteps <= 2 ? 1 : 2;
        if (BusyOverlayProgressValue < AnalyzeEtaMinimumProgressPercent || completedSteps < minimumCompletedSteps)
        {
            BusyOverlayEtaText = "Estimated time: estimating...";
            return;
        }

        var elapsedSeconds = _busyOperationStopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            BusyOverlayEtaText = "Estimated time: estimating...";
            return;
        }

        var remainingSteps = Math.Max(0, totalSteps - completedSteps);
        if (remainingSteps == 0)
        {
            BusyOverlayEtaText = string.Empty;
            return;
        }

        var rawEtaSeconds = (elapsedSeconds / completedSteps) * remainingSteps;
        if (double.IsNaN(rawEtaSeconds) || double.IsInfinity(rawEtaSeconds) || rawEtaSeconds < 0)
        {
            BusyOverlayEtaText = "Estimated time: estimating...";
            return;
        }

        SetAnalyzeEta(rawEtaSeconds);
    }

    private void SetAnalyzeEta(double rawEtaSeconds)
    {
        _busyOverlaySmoothedEtaSeconds = _busyOverlaySmoothedEtaSeconds is null
            ? rawEtaSeconds
            : (_busyOverlaySmoothedEtaSeconds.Value * (1 - AnalyzeEtaSmoothingFactor)) + (rawEtaSeconds * AnalyzeEtaSmoothingFactor);

        BusyOverlayEtaText = $"Estimated time: {FormatApproximateDuration(TimeSpan.FromSeconds(_busyOverlaySmoothedEtaSeconds.Value))}";
    }

    private TimeSpan GetHistoricalRemainingAnalyzeDuration(IReadOnlyList<string> destinationPathsToAnalyze, int currentIndex)
    {
        var remainingDuration = TimeSpan.Zero;

        for (var index = currentIndex + 1; index < destinationPathsToAnalyze.Count; index++)
        {
            var historyKey = CreateAnalyzeTimingHistoryKey(CreateConfiguration([destinationPathsToAnalyze[index]]));
            if (_analyzeTimingHistory.TryGetValue(historyKey, out var analyzeTimingHistory))
            {
                remainingDuration += analyzeTimingHistory.TotalDuration;
            }
        }

        return remainingDuration;
    }

    private static string CreateAnalyzeTimingHistoryKey(SyncConfiguration configuration)
    {
        return string.Join("|",
            NormalizeAnalyzeTimingPath(configuration.SourcePath),
            NormalizeAnalyzeTimingPath(configuration.DestinationPath),
            configuration.Mode,
            configuration.HideMacOsSystemFiles,
            configuration.IncludeSubfolders,
            configuration.DetectMoves);
    }

    private static string NormalizeAnalyzeTimingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().TrimEnd('/', '\\').ToUpperInvariant();
    }

    private string FormatPathForProgress(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "destination";
        }

        return _driveDisplayNameService.FormatDestinationPathForDisplay(path);
    }

    private static string AbbreviateAnalyzePath(string currentPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return "current item";
        }

        if (!string.IsNullOrWhiteSpace(rootPath) && currentPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = currentPath[rootPath.Length..].TrimStart('/', '\\');
            return string.IsNullOrWhiteSpace(relative)
                ? currentPath
                : $".../{relative.Replace('\\', '/')}";
        }

        return currentPath.Replace('\\', '/');
    }

    private static string FormatApproximateDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
        {
            return "< 1s";
        }

        if (duration.TotalSeconds < 10)
        {
            return $"~{Math.Ceiling(duration.TotalSeconds):0}s";
        }

        if (duration.TotalMinutes < 1)
        {
            var roundedSeconds = (int)(Math.Ceiling(duration.TotalSeconds / 5d) * 5);
            return $"~{roundedSeconds:0}s";
        }

        if (duration.TotalHours < 1)
        {
            var roundedSeconds = (int)(Math.Ceiling(duration.TotalSeconds / 15d) * 15);
            var rounded = TimeSpan.FromSeconds(roundedSeconds);
            var minutes = (int)rounded.TotalMinutes;
            var seconds = rounded.Seconds;
            return seconds == 0 ? $"~{minutes}m" : $"~{minutes}m {seconds:00}s";
        }

        var roundedMinutesTotal = (int)(Math.Ceiling(duration.TotalMinutes / 5d) * 5);
        var hours = roundedMinutesTotal / 60;
        var remainingMinutes = roundedMinutesTotal % 60;
        return remainingMinutes == 0
            ? $"~{hours}h"
            : $"~{hours}h {remainingMinutes}m";
    }

    private void LogSkippedDestinationPaths(IReadOnlyList<string> skippedDestinationPaths, string state)
    {
        foreach (var skippedDestinationPath in skippedDestinationPaths)
        {
            AddLog(
                state,
                $"Skipped '{_driveDisplayNameService.FormatDestinationPathForDisplay(skippedDestinationPath)}' because it matches the source path.",
                SyncLogSeverity.Warning);
        }
    }

    private void ReplaceActions(IEnumerable<SyncAction> actions)
    {
        var actionList = actions.ToList();

        RunOnDispatcher(() =>
        {
            ReplaceCollection(PlannedActions, actionList);
        });
    }

    private async Task ReplacePreviewAsync(IEnumerable<SyncPreviewItem> items, CancellationToken cancellationToken)
    {
        var previewItems = items.ToList();
        if (previewItems.Count == 0)
        {
            ReplacePreviewRows([]);
            return;
        }

        if (_busyOperationKind == BusyOperationKind.Analyze && IsBusy)
        {
            BusyOverlayDescription = previewItems.Count == 1
                ? "Preparing 1 preview row..."
                : $"Preparing {previewItems.Count:N0} preview rows...";
            await EnsureBusyOverlayCanRenderAsync().ConfigureAwait(true);
        }

        var rows = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectedRows = previewItems
                .Select(item => new SyncPreviewRowViewModel(item, driveDisplayNameService: _driveDisplayNameService))
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();
            return SortPreviewRows(projectedRows);
        }, cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        ReplacePreviewRows(rows);
    }

    private void ReplacePreviewRows(IReadOnlyList<SyncPreviewRowViewModel> rows)
    {
        RunOnDispatcher(() =>
        {
            foreach (var existingRow in _previewRowsByItemKey.Values)
            {
                existingRow.PropertyChanged -= OnPreviewRowPropertyChanged;
            }

            _previewRowsByItemKey.Clear();
            foreach (var row in rows)
            {
                _previewRowsByItemKey[row.ItemKey] = row;
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
        });
    }

    private void ReplaceQueue(IEnumerable<SyncAction> actions)
    {
        var queueActions = actions.ToList();

        RunOnDispatcher(() =>
        {
            ReplaceCollection(RemainingQueue, queueActions.Select(action => new QueueActionViewModel(action)).ToList());

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

    private bool? GetDriveToolDuplicateSelectionState()
    {
        var selectableRows = GetSelectableDriveToolDuplicateRows();
        if (selectableRows.Count == 0)
        {
            return false;
        }

        var selectedCount = selectableRows.Count(row => row.IsSelected);
        if (selectedCount == 0)
        {
            return false;
        }

        return selectedCount == selectableRows.Count ? true : null;
    }

    private void SetDriveToolDuplicateSelection(bool? isSelected)
    {
        UpdateDriveToolDuplicateSelection(_ => isSelected ?? false);
    }

    private List<DriveToolDuplicateRowViewModel> GetSelectableDriveToolDuplicateRows() =>
        DriveToolDuplicateRows.Where(row => row.CanSelect).ToList();

    private void UpdateDriveToolDuplicateSelection(Func<DriveToolDuplicateRowViewModel, bool> selectionFactory)
    {
        var selectableRows = GetSelectableDriveToolDuplicateRows();
        if (selectableRows.Count == 0)
        {
            return;
        }

        _suppressSelectionUpdates = true;
        foreach (var row in selectableRows)
        {
            row.IsSelected = selectionFactory(row);
        }
        _suppressSelectionUpdates = false;

        RefreshDriveToolDuplicateSelectionSafety(showDialog: true);
        RaisePropertyChanged(nameof(AreAllDriveToolDuplicatesSelected));
        DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
    }

    private static bool MatchesDriveToolPattern(DriveToolDuplicateRowViewModel row, string keyword, PreviewSelectionTarget target) => target switch
    {
        PreviewSelectionTarget.FileName => ContainsKeyword(row.FileName, keyword),
        PreviewSelectionTarget.FileFolder => ContainsKeyword(Path.GetDirectoryName(row.DisplayPath), keyword),
        _ => ContainsKeyword(row.DisplayPath, keyword),
    };

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

    private static string FormatDriveToolSize(long size)
    {
        if (size < 1024)
        {
            return $"{size} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB" };
        double value = size;
        var unitIndex = -1;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[Math.Max(unitIndex, 0)]}";
    }

    private void OnDriveToolDuplicateRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DriveToolDuplicateRowViewModel.IsSelected) ||
            e.PropertyName == nameof(DriveToolDuplicateRowViewModel.CanSelect))
        {
            if (!_suppressSelectionUpdates)
            {
                RefreshDriveToolDuplicateSelectionSafety(showDialog: true);
            }

            RaisePropertyChanged(nameof(AreAllDriveToolDuplicatesSelected));
            DeleteSelectedDuplicatesCommand.RaiseCanExecuteChanged();
        }
    }

    internal SyncPreviewRowViewModel? CreatePreviewDialogRow(object? parameter)
    {
        if (parameter is SyncPreviewRowViewModel previewRow)
        {
            return previewRow;
        }

        if (parameter is not DriveToolDuplicateRowViewModel duplicateRow ||
            duplicateRow.FileEntry is null ||
            string.IsNullOrWhiteSpace(duplicateRow.OpenPath))
        {
            return null;
        }

        return new SyncPreviewRowViewModel(
            new SyncPreviewItem(
                ItemKey: duplicateRow.ItemKey,
                RelativePath: duplicateRow.FileEntry.RelativePath,
                IsDirectory: false,
                SourceFullPath: duplicateRow.FileEntry.FullPath,
                SourceLength: duplicateRow.FileEntry.Length,
                SourceLastWriteTimeUtc: duplicateRow.FileEntry.LastWriteTimeUtc,
                DestinationFullPath: string.Empty,
                DestinationLength: null,
                DestinationLastWriteTimeUtc: null,
                Direction: string.Empty,
                Status: "Duplicate",
                Category: SyncPreviewCategory.UnchangedFiles,
                PlannedActionType: null,
                DriveLocationPath: DriveToolsPath),
            driveDisplayNameService: _driveDisplayNameService);
    }

    private static bool TryGetOpenPath(object? parameter, out string path)
    {
        path = parameter switch
        {
            SyncPreviewRowViewModel row => row.OpenPath,
            DriveToolDuplicateRowViewModel row => row.OpenPath,
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryGetExistingOpenPath(object? parameter, out string path)
    {
        if (TryGetOpenPath(parameter, out path) && File.Exists(path))
        {
            return true;
        }

        path = string.Empty;
        return false;
    }

    private void RefreshDriveToolDuplicateSelectionSafety(bool showDialog)
    {
        var groups = DriveToolDuplicateRows
            .Where(row => row.CanSelect)
            .GroupBy(row => row.GroupKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var conflictingGroupKeys = groups
            .Where(group => group.Any() && group.All(row => row.IsSelected))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newlyConflictedGroupCount = conflictingGroupKeys
            .Except(_conflictingDriveToolDuplicateGroupKeys, StringComparer.OrdinalIgnoreCase)
            .Count();

        foreach (var row in DriveToolDuplicateRows.Where(row => row.IsGroupHeader))
        {
            row.HasSelectionConflict = conflictingGroupKeys.Contains(row.GroupKey);
        }

        _conflictingDriveToolDuplicateGroupKeys = conflictingGroupKeys;
        HasDriveToolDuplicateSelectionConflict = conflictingGroupKeys.Count > 0;

        if (showDialog && newlyConflictedGroupCount > 0)
        {
            _userDialogService.ShowWarning(
                "All files selected",
                newlyConflictedGroupCount == 1
                    ? "All files in one duplicate group are selected for deletion."
                    : $"All files in {newlyConflictedGroupCount} duplicate groups are selected for deletion.");
        }
    }

    private static bool TryGetModifiableDriveToolDuplicate(object? parameter, out DriveToolDuplicateRowViewModel? duplicateRow)
    {
        if (parameter is DriveToolDuplicateRowViewModel { FileEntry: not null, HasOpenPath: true } row)
        {
            duplicateRow = row;
            return true;
        }

        duplicateRow = null;
        return false;
    }

    internal async Task<bool> RenameDriveToolDuplicateAsync(object? parameter, string newFileName)
    {
        if (!TryGetModifiableDriveToolDuplicate(parameter, out var duplicateRow))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(duplicateRow);

        var trimmedFileName = newFileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedFileName))
        {
            StatusMessage = "Enter a file name before renaming.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        if (trimmedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            StatusMessage = "The new file name contains invalid characters.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        var sourcePath = duplicateRow.OpenPath;
        var targetDirectory = Path.GetDirectoryName(sourcePath) ?? DriveToolsPath;
        var targetPath = Path.Combine(targetDirectory, trimmedFileName);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "The file already has that name.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        if (File.Exists(targetPath))
        {
            StatusMessage = "A file with that name already exists in the target folder.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        try
        {
            File.Move(sourcePath, targetPath);
            SetStatusMessage($"Renamed '{duplicateRow.FileName}' to '{trimmedFileName}'.", isSuccess: true);
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Verbose);
            await FindDuplicatesAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusMessage = $"Could not rename '{duplicateRow.FileName}'. {exception.Message}";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Error);
            return false;
        }
    }

    internal async Task<bool> MoveDriveToolDuplicateAsync(object? parameter, string destinationFolder)
    {
        if (!TryGetModifiableDriveToolDuplicate(parameter, out var duplicateRow))
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(duplicateRow);

        var trimmedDestinationFolder = destinationFolder?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedDestinationFolder))
        {
            StatusMessage = "Select a destination folder before moving the file.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        string normalizedDestinationFolder;
        try
        {
            normalizedDestinationFolder = Path.GetFullPath(trimmedDestinationFolder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusMessage = $"The selected destination folder is invalid. {exception.Message}";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        if (!Path.IsPathFullyQualified(normalizedDestinationFolder))
        {
            StatusMessage = "Select a fully qualified destination folder before moving the file.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        var sourcePath = duplicateRow.OpenPath;
        var targetPath = Path.Combine(normalizedDestinationFolder, duplicateRow.FileName);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "The file is already in that folder.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        if (File.Exists(targetPath))
        {
            StatusMessage = "A file with the same name already exists in the destination folder.";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Warning);
            return false;
        }

        try
        {
            Directory.CreateDirectory(normalizedDestinationFolder);
            File.Move(sourcePath, targetPath);
            SetStatusMessage($"Moved '{duplicateRow.FileName}' to '{normalizedDestinationFolder}'.", isSuccess: true);
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Verbose);
            await FindDuplicatesAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusMessage = $"Could not move '{duplicateRow.FileName}'. {exception.Message}";
            AddLog("Duplicate Finder", StatusMessage, SyncLogSeverity.Error);
            return false;
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
        ReplaceCollection(target, rows.ToList());
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target is BulkObservableCollection<T> bulkCollection)
        {
            bulkCollection.ReplaceAll(items);
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
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
        _isBusyOverlayDismissed = false;
        _useCompletedAnalyzeResultsRequested = false;
        _currentAnalyzeCompletedDestinationCount = 0;
        _currentAnalyzeTotalDestinationCount = 0;
        _busyOperationStopwatch.Restart();
        _busyOverlaySmoothedEtaSeconds = null;
        IsBusyOverlayProgressIndeterminate = busyOperationKind is BusyOperationKind.Analyze or BusyOperationKind.FindDuplicates;
        RaisePropertyChanged(nameof(IsBusyOverlayVisible));
        RaisePropertyChanged(nameof(IsUseCompletedAnalyzeResultsVisible));
        (BusyOverlayTitle, BusyOverlayDescription) = busyOperationKind switch
        {
            BusyOperationKind.Analyze => ("Loading preview...", "Building the synchronization preview."),
            BusyOperationKind.FindDuplicates => ("Finding duplicates...", "Hashing same-size files to confirm identical copies."),
            BusyOperationKind.Sync => ("Synchronizing...", "Applying the selected file operations."),
            _ => ("Working...", "Please wait."),
        };
        BusyOverlayCountersText = string.Empty;
        BusyOverlayPathText = string.Empty;
        BusyOverlayEtaText = string.Empty;
        CancelBusyOperationCommand.RaiseCanExecuteChanged();
        UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();
    }

    private void ClearBusyOperation(CancellationTokenSource cancellationTokenSource)
    {
        if (!ReferenceEquals(_busyOperationCancellationTokenSource, cancellationTokenSource))
        {
            return;
        }

        _busyOperationCancellationTokenSource = null;
        _busyOperationKind = BusyOperationKind.None;
        _isBusyOverlayDismissed = false;
        _useCompletedAnalyzeResultsRequested = false;
        _currentAnalyzeCompletedDestinationCount = 0;
        _currentAnalyzeTotalDestinationCount = 0;
        _busyOperationStopwatch.Reset();
        _busyOverlaySmoothedEtaSeconds = null;
        IsBusyOverlayProgressIndeterminate = false;
        BusyOverlayProgressValue = 0;
        RaisePropertyChanged(nameof(IsBusyOverlayVisible));
        RaisePropertyChanged(nameof(IsUseCompletedAnalyzeResultsVisible));
        BusyOverlayTitle = "Working...";
        BusyOverlayDescription = "Please wait.";
        BusyOverlayCountersText = string.Empty;
        BusyOverlayPathText = string.Empty;
        BusyOverlayEtaText = string.Empty;
        CancelBusyOperationCommand.RaiseCanExecuteChanged();
        UseCompletedAnalyzeResultsCommand.RaiseCanExecuteChanged();
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
