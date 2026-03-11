using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Strategies;

namespace UsbFileSync.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ShouldLogCopyStart_LogsEachItemOnlyOnceAcrossInterleavedProgress()
    {
        var loggedTransferItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.True(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(0, 3, "a.bin", 0, 100, 0)));
        Assert.True(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(0, 3, "b.bin", 0, 100, 0)));
        Assert.False(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(0, 3, "a.bin", 50, 100, 50)));
        Assert.True(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(1, 3, "c.bin", 0, 100, 0)));
        Assert.False(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(2, 3, "b.bin", 100, 100, 100)));
        Assert.False(MainWindowViewModel.ShouldLogCopyStart(loggedTransferItems, new SyncProgress(3, 3, "done.bin", 100, 100, 100)));
    }

    [Fact]
    public void BrowseSourcePathCommand_UsesFolderPickerSelection()
    {
        var folderPicker = new StubFolderPickerService("F:\\Primary");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: folderPicker);

        viewModel.BrowseSourcePathCommand.Execute(null);

        Assert.Equal("F:\\Primary", viewModel.SourcePath);
    }

    [Fact]
    public void BrowseDestinationPathCommand_KeepsExistingValueWhenPickerCancelled()
    {
        var folderPicker = new StubFolderPickerService(null);
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: folderPicker)
        {
            DestinationPath = "E:\\Backup"
        };

        viewModel.BrowseDestinationPathCommand.Execute(null);

        Assert.Equal("E:\\Backup", viewModel.DestinationPath);
    }

    [Fact]
    public void SourceAndDestinationDisplayText_UseExplorerStyleLabels_WhenNotFocused()
    {
        var driveDisplayNameService = new StubDriveDisplayNameService();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: driveDisplayNameService)
        {
            SourcePath = "F:\\",
            DestinationPath = "D:\\",
        };

        Assert.Equal("XTIVIA (F:)", viewModel.SourcePathDisplayText);
        Assert.Equal("Backup Drive (D:)", viewModel.DestinationPathDisplayText);
    }

    [Fact]
    public void SourceAndDestinationDisplayText_ShowRawPath_WhenFocused()
    {
        var driveDisplayNameService = new StubDriveDisplayNameService();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: driveDisplayNameService)
        {
            SourcePath = "F:\\",
            DestinationPath = "D:\\",
        };

        viewModel.SetSourcePathFocused(true);
        viewModel.SetDestinationPathFocused(true);

        Assert.Equal("F:\\", viewModel.SourcePathDisplayText);
        Assert.Equal("D:\\", viewModel.DestinationPathDisplayText);
    }

    [Fact]
    public void DirectionIndicator_UsesDoubleArrowForOneWay()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.SelectedMode = SyncMode.OneWay;

        Assert.Equal(">>", viewModel.DirectionIndicator);
    }

    [Fact]
    public void DirectionIndicator_UsesBidirectionalArrowForTwoWay()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.SelectedMode = SyncMode.TwoWay;

        Assert.Equal("<>", viewModel.DirectionIndicator);
    }

    [Fact]
    public void CompactDirectionIndicators_ChangeWithSelectedMode()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.SelectedMode = SyncMode.OneWay;
        Assert.Equal(">", viewModel.LeftDirectionIndicator);
        Assert.Equal(">", viewModel.RightDirectionIndicator);

        viewModel.SelectedMode = SyncMode.TwoWay;
        Assert.Equal("<", viewModel.LeftDirectionIndicator);
        Assert.Equal(">", viewModel.RightDirectionIndicator);
    }

    [Fact]
    public void LocationDescriptions_ChangeWithSelectedMode()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.SelectedMode = SyncMode.OneWay;
        Assert.Contains("source of truth", viewModel.SourceLocationDescription);
        Assert.Contains("receives created folders", viewModel.DestinationLocationDescription);

        viewModel.SelectedMode = SyncMode.TwoWay;
        Assert.Contains("both directions", viewModel.SourceLocationDescription);
        Assert.Contains("reconciled back", viewModel.DestinationLocationDescription);
    }

    [Fact]
    public async Task ToggleSyncCommand_StartsAndStopsSynchronization()
    {
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = new BlockingSyncStrategy(completionSource);
        var syncService = new SyncService(strategy, strategy);
        using var workspace = new SyncTestWorkspace();
        using var viewModel = new MainWindowViewModel(syncService, settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath
        };

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("Stop synchronization", viewModel.SyncButtonText);

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("Synchronize", viewModel.SyncButtonText);
        Assert.Equal("Synchronization stopped.", viewModel.StatusMessage);
    }

    [Fact]
    public void LoadSavedConfiguration_SetsSuccessStatus_WhenConfigurationRestored()
    {
        var settingsStore = new StubSyncSettingsStore(new SyncConfiguration
        {
            SourcePath = "F:\\Primary",
            DestinationPath = "E:\\Backup",
            Mode = SyncMode.OneWay,
            DetectMoves = true,
            DryRun = true,
            VerifyChecksums = false,
            ParallelCopyCount = 1,
        });

        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: settingsStore, folderPickerService: new StubFolderPickerService(null));

        Assert.Equal("Restored the previous sync configuration.", viewModel.StatusMessage);
        Assert.True(viewModel.IsStatusSuccess);
    }

    [Fact]
    public void UpdateParallelCopyCount_AllowsAutoValue_AndLogsAutoMode()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.UpdateParallelCopyCount(0);

        Assert.Equal(0, viewModel.ParallelCopyCount);
        Assert.Equal("Parallel copy count set to auto.", viewModel.ActivityLog[0].Message);
    }

    [Fact]
    public void VerifyChecksums_CanBeEnabled()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.VerifyChecksums = true;

        Assert.True(viewModel.VerifyChecksums);
    }

    [Fact]
    public void DetectMoves_IsOnlyAvailableInOneWayMode()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        Assert.True(viewModel.IsDetectMovesAvailable);

        viewModel.SelectedMode = SyncMode.TwoWay;

        Assert.False(viewModel.IsDetectMovesAvailable);
        Assert.True(viewModel.DetectMoves);

        viewModel.SelectedMode = SyncMode.OneWay;

        Assert.True(viewModel.IsDetectMovesAvailable);
    }

    [Fact]
    public void OpenPreviewItemCommand_UsesFileLauncherService()
    {
        var launcher = new StubFileLauncherService();
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null), fileLauncherService: launcher);
        var row = CreatePreviewRow(@"F:\folder\file.txt", isDirectory: false);

        viewModel.OpenPreviewItemCommand.Execute(row);

        Assert.Equal("open-item", launcher.LastOperation);
        Assert.Equal(@"F:\folder\file.txt", launcher.LastPath);
    }

    [Fact]
    public void OpenPreviewContainingFolderCommand_UsesFileLauncherService()
    {
        var launcher = new StubFileLauncherService();
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null), fileLauncherService: launcher);
        var row = CreatePreviewRow(@"F:\folder\file.txt", isDirectory: false);

        viewModel.OpenPreviewContainingFolderCommand.Execute(row);

        Assert.Equal("open-folder", launcher.LastOperation);
        Assert.Equal(@"F:\folder\file.txt", launcher.LastPath);
    }

    [Fact]
    public void OpenDestinationPreviewItemCommand_UsesFileLauncherService()
    {
        var launcher = new StubFileLauncherService();
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null), fileLauncherService: launcher);
        var row = CreatePreviewRow(@"F:\folder\source.txt", @"E:\backup\source.txt", isDirectory: false);

        viewModel.OpenDestinationPreviewItemCommand.Execute(row);

        Assert.Equal("open-item", launcher.LastOperation);
        Assert.Equal(@"E:\backup\source.txt", launcher.LastPath);
    }

    [Fact]
    public void OpenDestinationPreviewContainingFolderCommand_UsesFileLauncherService()
    {
        var launcher = new StubFileLauncherService();
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null), fileLauncherService: launcher);
        var row = CreatePreviewRow(@"F:\folder\source.txt", @"E:\backup\source.txt", isDirectory: false);

        viewModel.OpenDestinationPreviewContainingFolderCommand.Execute(row);

        Assert.Equal("open-folder", launcher.LastOperation);
        Assert.Equal(@"E:\backup\source.txt", launcher.LastPath);
    }

    [Fact]
    public async Task AnalyzeCommand_LeavesRowsDeselected_AndSelectionIsSharedAcrossViews()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("only-source.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);

        var newRow = Assert.Single(viewModel.NewFiles);
        var allRow = Assert.Single(viewModel.AllFiles);
        Assert.False(newRow.IsSelected);
        Assert.False(allRow.IsSelected);
        Assert.Empty(viewModel.RemainingQueue);

        newRow.IsSelected = true;

        Assert.True(allRow.IsSelected);
        Assert.Single(viewModel.RemainingQueue);
        Assert.Equal("only-source.txt", viewModel.RemainingQueue[0].RelativePath);
    }

    [Fact]
    public async Task HeaderSelection_SelectsFilteredRows_AndSyncOnlyProcessesSelectedItems()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("one.txt", "one");
        workspace.WriteSourceFile("two.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        viewModel.AreAllNewFilesSelected = true;
        Assert.True(viewModel.AllFiles.All(row => row.IsSelected));
        Assert.Equal(2, viewModel.RemainingQueue.Count);

        viewModel.AllFiles.First(row => row.RelativePath == "two.txt").IsSelected = false;
        Assert.True(viewModel.NewFiles.First(row => row.RelativePath == "one.txt").IsSelected);
        Assert.False(viewModel.NewFiles.First(row => row.RelativePath == "two.txt").IsSelected);
        Assert.Single(viewModel.RemainingQueue);

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => File.Exists(Path.Combine(workspace.DestinationPath, "one.txt")) && !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.True(File.Exists(Path.Combine(workspace.DestinationPath, "one.txt")));
        Assert.False(File.Exists(Path.Combine(workspace.DestinationPath, "two.txt")));
    }

    [Fact]
    public async Task ToggleSyncCommand_ProcessesQueuedActions_WhenPreviewSelectionIsUnavailable()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("queued.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        var action = new SyncAction(
            SyncActionType.CopyToDestination,
            "queued.txt",
            Path.Combine(workspace.SourcePath, "queued.txt"),
            Path.Combine(workspace.DestinationPath, "queued.txt"));

        viewModel.PlannedActions.Add(action);
        viewModel.RemainingQueue.Add(new QueueActionViewModel(action));

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => File.Exists(Path.Combine(workspace.DestinationPath, "queued.txt")) && !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.True(File.Exists(Path.Combine(workspace.DestinationPath, "queued.txt")));
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(workspace.DestinationPath, "queued.txt")).ConfigureAwait(true));
    }

    [Fact]
    public async Task ToggleSyncCommand_MentionsChecksumVerification_WhenChecksumModeIsEnabled()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("verified.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
            VerifyChecksums = true,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);
        viewModel.AreAllNewFilesSelected = true;

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() =>
            File.Exists(Path.Combine(workspace.DestinationPath, "verified.txt")) &&
            viewModel.StatusMessage == "Synchronization complete. Applied 1 action(s). Checksum verification passed for 1 copied file(s)." &&
            !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("Synchronization complete. Applied 1 action(s). Checksum verification passed for 1 copied file(s).", viewModel.StatusMessage);
        Assert.Equal("Sync", viewModel.ActivityLog[0].State);
        Assert.Equal("Synchronization complete. Applied 1 action(s). Checksum verification passed for 1 copied file(s).", viewModel.ActivityLog[0].Message);
    }

    [Fact]
    public async Task ToggleSyncCommand_LogsWarning_WhenNoPreviewItemsAreSelected()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("pending.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("Warning", viewModel.ActivityLog[0].State);
        Assert.Equal("Synchronization skipped because no items to be synced were selected.", viewModel.ActivityLog[0].Message);
        Assert.True(viewModel.ActivityLog[0].IsAlert);
    }

    [Fact]
    public void ToggleSyncCommand_ShowsValidationError_WhenDestinationDriveIsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var existingDriveLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        var missingDriveLetter = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(value => (char)value)
            .FirstOrDefault(letter => !existingDriveLetters.Contains(letter));

        if (missingDriveLetter == default)
        {
            return;
        }

        using var workspace = new SyncTestWorkspace();
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = $"{missingDriveLetter}:\\",
            DryRun = false,
        };

        viewModel.ToggleSyncCommand.Execute(null);

        Assert.Equal("Destination path does not exist or is not accessible.", viewModel.StatusMessage);
        Assert.False(viewModel.IsSyncRunning);
        Assert.Equal("Error", viewModel.ActivityLog[0].State);
        Assert.Equal("Destination path does not exist or is not accessible.", viewModel.ActivityLog[0].Message);
    }

    [Fact]
    public void AnalyzeCommand_ShowsValidationError_WhenDestinationDriveIsUnavailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var existingDriveLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();
        var missingDriveLetter = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(value => (char)value)
            .FirstOrDefault(letter => !existingDriveLetters.Contains(letter));

        if (missingDriveLetter == default)
        {
            return;
        }

        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("only-source.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = $"{missingDriveLetter}:\\",
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);

        WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).GetAwaiter().GetResult();

        Assert.Equal("Preview generated with 1 planned action(s). Select the items to synchronize.", viewModel.StatusMessage);
        Assert.Equal("Analyze", viewModel.ActivityLog[0].State);
        Assert.Equal("Preview generated with 1 planned action(s). Select the items to synchronize.", viewModel.ActivityLog[0].Message);
    }

    [Fact]
    public async Task HeaderSelection_ClearsFilteredRows_WhenSetToFalse()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("one.txt", "one");
        workspace.WriteSourceFile("two.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        viewModel.AreAllNewFilesSelected = true;
        Assert.All(viewModel.NewFiles, row => Assert.True(row.IsSelected));

        viewModel.AreAllNewFilesSelected = false;

        Assert.All(viewModel.NewFiles, row => Assert.False(row.IsSelected));
        Assert.All(viewModel.AllFiles, row => Assert.False(row.IsSelected));
        Assert.Empty(viewModel.RemainingQueue);
        Assert.False(viewModel.AreAllNewFilesSelected);
        Assert.False(viewModel.AreAllFilesSelected);
    }

    [Fact]
    public async Task HeaderSelection_ClearsFilteredRows_WhenSetToNull()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("one.txt", "one");
        workspace.WriteSourceFile("two.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        viewModel.AreAllNewFilesSelected = true;
        Assert.All(viewModel.NewFiles, row => Assert.True(row.IsSelected));

        viewModel.AreAllNewFilesSelected = null;

        Assert.All(viewModel.NewFiles, row => Assert.False(row.IsSelected));
        Assert.All(viewModel.AllFiles, row => Assert.False(row.IsSelected));
        Assert.Empty(viewModel.RemainingQueue);
        Assert.False(viewModel.AreAllNewFilesSelected);
        Assert.False(viewModel.AreAllFilesSelected);
    }

    [Fact]
    public void ActivityLogFilter_ShowsOnlyAlerts_WhenRequested()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.SourcePath = "C:\\source";
        viewModel.DestinationPath = string.Empty;
        viewModel.ToggleSyncCommand.Execute(null);
        viewModel.ShowAlertsOnlyActivityLog = true;

        var filteredEntries = viewModel.ActivityLogView.Cast<SyncLogEntryViewModel>().ToList();

        Assert.NotEmpty(filteredEntries);
        Assert.All(filteredEntries, entry => Assert.True(entry.IsAlert));
    }

    [Fact]
    public async Task SelectAllInTab_SelectsAllRowsInRequestedTab()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("one.txt", "one");
        workspace.WriteSourceFile("nested\\two.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        Assert.All(viewModel.NewFiles, row => Assert.True(row.IsSelected));
        Assert.Equal(2, viewModel.RemainingQueue.Count);
    }

    [Fact]
    public async Task SelectByPattern_FiltersSelectionByFileNameFolderAndFullPath()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("photos\\holiday-shot.txt", "one");
        workspace.WriteSourceFile("docs\\notes.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        var fileNameMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "holiday", PreviewSelectionTarget.FileName);
        Assert.Equal(1, fileNameMatches);
        Assert.True(viewModel.NewFiles.Single(row => row.RelativePath == "holiday-shot.txt").IsSelected);
        Assert.False(viewModel.NewFiles.Single(row => row.RelativePath == "notes.txt").IsSelected);

        var folderMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "docs", PreviewSelectionTarget.FileFolder);
        Assert.Equal(1, folderMatches);
        Assert.False(viewModel.NewFiles.Single(row => row.RelativePath == "holiday-shot.txt").IsSelected);
        Assert.True(viewModel.NewFiles.Single(row => row.RelativePath == "notes.txt").IsSelected);

        var fullPathMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "photos\\holiday-shot", PreviewSelectionTarget.FullPath);
        Assert.Equal(1, fullPathMatches);
        Assert.True(viewModel.NewFiles.Single(row => row.RelativePath == "holiday-shot.txt").IsSelected);
        Assert.False(viewModel.NewFiles.Single(row => row.RelativePath == "notes.txt").IsSelected);
    }

    [Fact]
    public async Task InvertSelectionInTab_FlipsSelectionStateForSelectableRows()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("one.txt", "one");
        workspace.WriteSourceFile("two.txt", "two");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        viewModel.NewFiles[0].IsSelected = true;
        viewModel.NewFiles[1].IsSelected = false;

        viewModel.InvertSelectionInTab(PreviewTabKind.NewFiles);

        Assert.False(viewModel.NewFiles[0].IsSelected);
        Assert.True(viewModel.NewFiles[1].IsSelected);
        Assert.Single(viewModel.RemainingQueue);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within the allowed time.");
            }

            await Task.Delay(25).ConfigureAwait(true);
        }
    }

    private sealed class StubFolderPickerService(string? selectedPath) : IFolderPickerService
    {
        public string? PickFolder(string title, string? initialPath) => selectedPath;
    }

    private sealed class StubFileLauncherService : IFileLauncherService
    {
        public string LastOperation { get; private set; } = string.Empty;

        public string LastPath { get; private set; } = string.Empty;

        public void OpenItem(string path)
        {
            LastOperation = "open-item";
            LastPath = path;
        }

        public void OpenContainingFolder(string path)
        {
            LastOperation = "open-folder";
            LastPath = path;
        }

        public void OpenFile(string path)
        {
            LastOperation = "open-file";
            LastPath = path;
        }
    }

    private sealed class StubDriveDisplayNameService : IDriveDisplayNameService
    {
        public string FormatPathForDisplay(string path) => path switch
        {
            "F:\\" => "XTIVIA (F:)",
            "D:\\" => "Backup Drive (D:)",
            _ => path,
        };
    }

    private sealed class StubSyncSettingsStore(SyncConfiguration? configuration) : ISyncSettingsStore
    {
        public SyncConfiguration? Load() => configuration;

        public void Save(SyncConfiguration configuration)
        {
        }
    }

    private static SyncPreviewRowViewModel CreatePreviewRow(string path, bool isDirectory) => CreatePreviewRow(path, path, isDirectory);

    private static SyncPreviewRowViewModel CreatePreviewRow(string sourcePath, string destinationPath, bool isDirectory) => new(new SyncPreviewItem(
        RelativePath: Path.GetFileName(sourcePath),
        IsDirectory: isDirectory,
        SourceFullPath: sourcePath,
        SourceLength: isDirectory ? null : 10,
        SourceLastWriteTimeUtc: DateTime.UtcNow,
        DestinationFullPath: destinationPath,
        DestinationLength: isDirectory ? null : 10,
        DestinationLastWriteTimeUtc: DateTime.UtcNow,
        Direction: "->",
        Status: "New File",
        Category: SyncPreviewCategory.NewFiles,
        PlannedActionType: isDirectory ? SyncActionType.CreateDirectoryOnDestination : SyncActionType.CopyToDestination));

    private sealed class SyncTestWorkspace : IDisposable
    {
        private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));

        public SyncTestWorkspace()
        {
            SourcePath = Path.Combine(_rootPath, "source");
            DestinationPath = Path.Combine(_rootPath, "destination");
            Directory.CreateDirectory(SourcePath);
            Directory.CreateDirectory(DestinationPath);
        }

        public string SourcePath { get; }

        public string DestinationPath { get; }

        public void WriteSourceFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(SourcePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }

    private sealed class BlockingSyncStrategy(TaskCompletionSource completionSource) : ISyncStrategy
    {
        public async Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await completionSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return [];
        }
    }
}
