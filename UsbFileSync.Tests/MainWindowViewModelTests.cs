using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System.ComponentModel;
using System.IO.Compression;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Strategies;
using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;
using WordRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

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
    public void BrowseDestinationPathCommand_UpdatesAdditionalDestination()
    {
        var folderPicker = new StubFolderPickerService("G:\\Archive");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: folderPicker);
        viewModel.AddDestinationPathCommand.Execute(null);

        var additionalDestination = Assert.Single(viewModel.AdditionalDestinationPaths);
        viewModel.BrowseDestinationPathCommand.Execute(additionalDestination);

        Assert.Equal("G:\\Archive", additionalDestination.Path);
    }

    [Fact]
    public async Task AnalyzeCommand_CanBeCancelled_WhileBuildingPreview()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("pending.txt", "payload");
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new MainWindowViewModel(
            new SyncService(new BlockingSyncStrategy(completionSource), new BlockingSyncStrategy(completionSource)),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
        };

        viewModel.AnalyzeCommand.Execute(null);

        await WaitForAsync(() => viewModel.IsBusy).ConfigureAwait(true);
        Assert.True(viewModel.CancelBusyOperationCommand.CanExecute(null));
        Assert.True(viewModel.IsBusyOverlayVisible);

        viewModel.CancelBusyOperationCommand.Execute(null);

        Assert.False(viewModel.IsBusyOverlayVisible);

        await WaitForAsync(() => !viewModel.IsBusy).ConfigureAwait(true);

        Assert.Equal("Preview loading cancelled.", viewModel.StatusMessage);
        Assert.Empty(viewModel.PlannedActions);
        Assert.Empty(viewModel.AllFiles);
    }

    [Fact]
    public async Task AnalyzeCommand_Cancel_ReenablesCommands_EvenWhenBackgroundAnalyzeFinishesLater()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("pending.txt", "payload");
        var completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewModel = new MainWindowViewModel(
            new SyncService(new IgnoreCancellationBlockingSyncStrategy(completionSource), new IgnoreCancellationBlockingSyncStrategy(completionSource)),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
        };

        viewModel.AnalyzeCommand.Execute(null);

        await WaitForAsync(() => viewModel.IsBusy).ConfigureAwait(true);
        viewModel.CancelBusyOperationCommand.Execute(null);

        await WaitForAsync(() => !viewModel.IsBusy).ConfigureAwait(true);
        await WaitForAsync(() => viewModel.AnalyzeCommand.CanExecute(null)).ConfigureAwait(true);

        Assert.True(viewModel.ToggleSyncCommand.CanExecute(null));
        Assert.Equal("Preview loading cancelled.", viewModel.StatusMessage);
        Assert.Empty(viewModel.PlannedActions);
        Assert.Empty(viewModel.AllFiles);

        completionSource.SetResult();
        await Task.Delay(100).ConfigureAwait(true);

        Assert.Empty(viewModel.PlannedActions);
        Assert.Empty(viewModel.AllFiles);
        Assert.True(viewModel.AnalyzeCommand.CanExecute(null));
        Assert.True(viewModel.ToggleSyncCommand.CanExecute(null));
    }

    [Fact]
    public async Task ToggleSyncCommand_CopiesFilesFromResolvedHfsPlusSourceVolume()
    {
        using var workspace = new SyncTestWorkspace();
        using var hfsWorkspace = new SyncTestWorkspace();
        hfsWorkspace.WriteSourceFile("song.txt", "music");

        var hfsSourceVolume = new StubVolumeSource("D:\\", hfsWorkspace.SourcePath, isReadOnly: true, fileSystemType: "HFS+");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            sourceVolumeService: new StubSourceVolumeService(hfsSourceVolume))
        {
            SourcePath = "D:\\",
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1).ConfigureAwait(true);
        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        viewModel.ToggleSyncCommand.Execute(null);

        await WaitForAsync(() => !viewModel.IsSyncRunning && File.Exists(Path.Combine(workspace.DestinationPath, "song.txt"))).ConfigureAwait(true);

        Assert.Equal("music", await File.ReadAllTextAsync(Path.Combine(workspace.DestinationPath, "song.txt")).ConfigureAwait(true));
    }

    [Fact]
    public async Task ToggleSyncCommand_CopiesFilesToResolvedExtDestinationVolume()
    {
        using var workspace = new SyncTestWorkspace();
        using var extWorkspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("report.txt", "payload");

        var extDestinationVolume = new StubVolumeSource("D:\\", extWorkspace.DestinationPath, isReadOnly: false, fileSystemType: "ext4");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            destinationVolumeService: new StubSourceVolumeService(extDestinationVolume))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = "D:\\",
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1).ConfigureAwait(true);
        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        viewModel.ToggleSyncCommand.Execute(null);

        var copiedFile = Path.Combine(extWorkspace.DestinationPath, "report.txt");
        await WaitForAsync(() => !viewModel.IsSyncRunning && File.Exists(copiedFile)).ConfigureAwait(true);

        Assert.Equal("payload", await File.ReadAllTextAsync(copiedFile).ConfigureAwait(true));
    }

    [Fact]
    public async Task ToggleSyncCommand_UsesExecutionClient_WhenExtDestinationRequiresElevation()
    {
        using var workspace = new SyncTestWorkspace();
        using var extWorkspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("report.txt", "payload");

        var readOnlyExtDestinationVolume = new StubVolumeSource("D:\\", extWorkspace.DestinationPath, isReadOnly: true, fileSystemType: "ext4");
        var executionClient = new StubSyncExecutionClient();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            destinationVolumeService: new StubSourceVolumeService(readOnlyExtDestinationVolume),
            syncExecutionClient: executionClient)
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = "D:\\",
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1).ConfigureAwait(true);
        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        viewModel.ToggleSyncCommand.Execute(null);

        await WaitForAsync(() => !viewModel.IsSyncRunning && executionClient.InvocationCount == 1).ConfigureAwait(true);

        Assert.Equal(1, executionClient.InvocationCount);
        Assert.Contains("Synchronization complete.", viewModel.StatusMessage);
        Assert.False(viewModel.IsSyncRunning);
    }

    [Fact]
    public void ToggleSyncCommand_ShowsHfsPlusValidationError_WhenHfsVolumeCannotOpenDrive()
    {
        using var workspace = new SyncTestWorkspace();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            sourceVolumeService: new StubSourceVolumeService(null, "The selected drive 'D:\\' does not appear to contain an HFS+ volume."))
        {
            SourcePath = "D:\\",
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.ToggleSyncCommand.Execute(null);

        Assert.Equal("Source volume could not be opened. The selected drive 'D:\\' does not appear to contain an HFS+ volume.", viewModel.StatusMessage);
        Assert.False(viewModel.IsSyncRunning);
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
    public void SourceAndDestinationDisplayText_FormatOneDriveLikeGoogleDrive_WhenNotFocused()
    {
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new WindowsDriveDisplayNameService())
        {
            SourcePath = "onedrive://root",
            DestinationPath = "onedrive://root/USBTest",
        };

        Assert.Equal("OneDrive", viewModel.SourcePathDisplayText);
        Assert.Equal("OneDrive / USBTest", viewModel.DestinationPathDisplayText);
    }

    [Fact]
    public void SourceAndDestinationDisplayText_ShowRawOneDrivePath_WhenFocused()
    {
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new WindowsDriveDisplayNameService())
        {
            SourcePath = "onedrive://root",
            DestinationPath = "onedrive://root/USBTest",
        };

        viewModel.SetSourcePathFocused(true);
        viewModel.SetDestinationPathFocused(true);

        Assert.Equal("onedrive://root", viewModel.SourcePathDisplayText);
        Assert.Equal("onedrive://root/USBTest", viewModel.DestinationPathDisplayText);
    }

    [Fact]
    public void SourceAndDestinationDisplayText_UsesCloudAliases_ForAccountScopedPaths()
    {
        var driveDisplayNameService = new WindowsDriveDisplayNameService(() =>
        [
            new CloudProviderAppRegistration
            {
                RegistrationId = "onedrive-account",
                Provider = CloudStorageProvider.OneDrive,
                Alias = "Personal OneDrive",
                ClientId = "onedrive-client-id",
                TenantId = "common"
            },
            new CloudProviderAppRegistration
            {
                RegistrationId = "dropbox-account",
                Provider = CloudStorageProvider.Dropbox,
                Alias = "Team Dropbox",
                ClientId = "dropbox-app-key"
            }
        ]);

        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: driveDisplayNameService)
        {
            SourcePath = OneDrivePath.BuildPath("onedrive-account", "USBTest"),
            DestinationPath = DropboxPath.BuildPath("dropbox-account", "Archive"),
        };

        Assert.Equal("OneDrive - Personal OneDrive / USBTest", viewModel.SourcePathDisplayText);
        Assert.Equal("Dropbox - Team Dropbox / Archive", viewModel.DestinationPathDisplayText);
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
    public async Task ToggleSyncCommand_CancelsExecutionClientSync()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("queued.txt", "payload");
        var executionClient = new BlockingSyncExecutionClient();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            syncExecutionClient: executionClient)
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1).ConfigureAwait(true);
        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => viewModel.IsSyncRunning && executionClient.Started).ConfigureAwait(true);

        viewModel.ToggleSyncCommand.Execute(null);

        await WaitForAsync(() => !viewModel.IsSyncRunning && executionClient.CancelObserved).ConfigureAwait(true);

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
    public void LoadSavedConfiguration_RestoresAdditionalDestinationPaths()
    {
        var settingsStore = new StubSyncSettingsStore(new SyncConfiguration
        {
            SourcePath = "F:\\Primary",
            DestinationPath = "E:\\Backup",
            DestinationPaths = ["E:\\Backup", "G:\\Archive"],
            Mode = SyncMode.OneWay,
        });

        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: settingsStore, folderPickerService: new StubFolderPickerService(null));

        Assert.Equal("E:\\Backup", viewModel.DestinationPath);
        Assert.Single(viewModel.AdditionalDestinationPaths);
        Assert.Equal("G:\\Archive", viewModel.AdditionalDestinationPaths[0].Path);
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
    public void MoveMode_CanBeEnabled()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.MoveMode = true;

        Assert.True(viewModel.MoveMode);
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
    public async Task ToggleSyncCommand_PreservesRemainingPreviewItems_AfterPartialSync()
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

        var analyzeLogCount = viewModel.ActivityLog.Count(entry => entry.State == "Analyze");
        viewModel.NewFiles.Single(row => row.RelativePath == "one.txt").IsSelected = true;

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsSyncRunning && File.Exists(Path.Combine(workspace.DestinationPath, "one.txt"))).ConfigureAwait(true);

        var firstCompletedRow = Assert.Single(viewModel.NewFiles.Where(row => row.RelativePath == "one.txt"));
        var remainingRow = Assert.Single(viewModel.NewFiles.Where(row => row.RelativePath == "two.txt"));

        Assert.Single(viewModel.PlannedActions);
        Assert.Equal(2, viewModel.NewFiles.Count);
        Assert.False(firstCompletedRow.CanSelect);
        Assert.False(firstCompletedRow.IsSelected);
        Assert.Equal("Done", firstCompletedRow.ProgressStateText);
        Assert.True(remainingRow.CanSelect);
        Assert.Equal(analyzeLogCount, viewModel.ActivityLog.Count(entry => entry.State == "Analyze"));

        remainingRow.IsSelected = true;
        Assert.Single(viewModel.RemainingQueue);
        Assert.Equal("two.txt", viewModel.RemainingQueue[0].RelativePath);

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => !viewModel.IsSyncRunning && File.Exists(Path.Combine(workspace.DestinationPath, "two.txt"))).ConfigureAwait(true);

        Assert.Equal(analyzeLogCount, viewModel.ActivityLog.Count(entry => entry.State == "Analyze"));
        Assert.Empty(viewModel.PlannedActions);
        Assert.Equal(2, viewModel.NewFiles.Count);
        Assert.All(viewModel.NewFiles, row =>
        {
            Assert.False(row.CanSelect);
            Assert.Equal("Done", row.ProgressStateText);
        });
        Assert.Empty(viewModel.RemainingQueue);
    }

    [Fact]
    public async Task AnalyzeAndSynchronize_ProcessesAdditionalDestinationsAsSeparatePreviewRows()
    {
        using var workspace = new SyncTestWorkspace();
        var secondDestinationPath = workspace.CreateAdditionalDestination("destination-two");
        workspace.WriteSourceFile("shared.txt", "payload");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new StubDriveDisplayNameService())
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AddDestinationPathCommand.Execute(null);
        var additionalDestination = Assert.Single(viewModel.AdditionalDestinationPaths);
        additionalDestination.Path = secondDestinationPath;

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2 && viewModel.AllFilesCount == 2).ConfigureAwait(true);

        Assert.Equal(2, viewModel.NewFiles.Select(row => row.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        viewModel.AreAllNewFilesSelected = true;
        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() =>
            File.Exists(Path.Combine(workspace.DestinationPath, "shared.txt")) &&
            File.Exists(Path.Combine(secondDestinationPath, "shared.txt")) &&
            !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(workspace.DestinationPath, "shared.txt")).ConfigureAwait(true));
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(secondDestinationPath, "shared.txt")).ConfigureAwait(true));
    }

    [Fact]
    public async Task PreviewColumnFilter_BuildsDestinationFileOptions_AndTracksActiveFilterState()
    {
        using var workspace = new SyncTestWorkspace();
        var secondDestinationPath = workspace.CreateAdditionalDestination("destination-two");
        workspace.WriteSourceFile("shared.txt", "payload");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new StubDriveDisplayNameService())
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AddDestinationPathCommand.Execute(null);
        var additionalDestination = Assert.Single(viewModel.AdditionalDestinationPaths);
        additionalDestination.Path = secondDestinationPath;

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2).ConfigureAwait(true);

        viewModel.OpenPreviewColumnFilter(
            PreviewTabKind.NewFiles,
            new PreviewColumnHeader { Title = "Destination File", ColumnKey = PreviewColumnKey.DestinationFile });
        viewModel.SetAllVisiblePreviewFilterOptions(false);
        var options = viewModel.PreviewFilterOptions.Where(option => option.Value != "(blank)").ToList();
        Assert.Equal(2, options.Count);
        options[0].IsSelected = true;

        viewModel.ApplyActivePreviewColumnFilter();

        Assert.True(viewModel.HasActivePreviewFilters);
        Assert.True(options[0].IsSelected);
        Assert.False(options[1].IsSelected);
    }

    [Fact]
    public async Task ClearActivePreviewColumnFilter_ClearsActiveFilterState()
    {
        using var workspace = new SyncTestWorkspace();
        var secondDestinationPath = workspace.CreateAdditionalDestination("destination-two");
        workspace.WriteSourceFile("shared.txt", "payload");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new StubDriveDisplayNameService())
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AddDestinationPathCommand.Execute(null);
        var additionalDestination = Assert.Single(viewModel.AdditionalDestinationPaths);
        additionalDestination.Path = secondDestinationPath;

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2).ConfigureAwait(true);

        viewModel.OpenPreviewColumnFilter(
            PreviewTabKind.NewFiles,
            new PreviewColumnHeader { Title = "Destination File", ColumnKey = PreviewColumnKey.DestinationFile });
        viewModel.SetAllVisiblePreviewFilterOptions(false);
        var secondOption = viewModel.PreviewFilterOptions.Where(option => option.Value != "(blank)").Last();
        secondOption.IsSelected = true;
        viewModel.ApplyActivePreviewColumnFilter();

        viewModel.OpenPreviewColumnFilter(
            PreviewTabKind.NewFiles,
            new PreviewColumnHeader { Title = "Destination File", ColumnKey = PreviewColumnKey.DestinationFile });
        viewModel.ClearActivePreviewColumnFilter();

        Assert.False(viewModel.HasActivePreviewFilters);
        Assert.All(viewModel.PreviewFilterOptions, option => Assert.True(option.IsSelected));
    }

    [Fact]
    public async Task PreviewColumnFilter_DeselectNonShown_OnlyClearsFilteredOutOptions()
    {
        using var workspace = new SyncTestWorkspace();
        var secondDestinationPath = workspace.CreateAdditionalDestination("destination-two");
        workspace.WriteSourceFile("shared.txt", "payload");
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            driveDisplayNameService: new StubDriveDisplayNameService())
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AddDestinationPathCommand.Execute(null);
        var additionalDestination = Assert.Single(viewModel.AdditionalDestinationPaths);
        additionalDestination.Path = secondDestinationPath;

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2).ConfigureAwait(true);

        viewModel.OpenPreviewColumnFilter(
            PreviewTabKind.NewFiles,
            new PreviewColumnHeader { Title = "Destination File", ColumnKey = PreviewColumnKey.DestinationFile });
        var allOptions = viewModel.PreviewFilterOptions.Where(option => option.Value != "(blank)").ToList();
        viewModel.PreviewFilterSearchText = allOptions.Last().Value;

        viewModel.SetAllNonShownPreviewFilterOptions(false);

        Assert.True(viewModel.PreviewFilterOptions.Single(option => option.Value == allOptions.Last().Value).IsSelected);
        Assert.False(viewModel.PreviewFilterOptions.Single(option => option.Value == allOptions.First().Value).IsSelected);
    }

    [Fact]
    public async Task SortActivePreviewColumn_SortsPreviewRowsBySourceFileDirection()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("b.txt", "two");
        workspace.WriteSourceFile("a.txt", "one");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 2).ConfigureAwait(true);

        viewModel.OpenPreviewColumnFilter(
            PreviewTabKind.NewFiles,
            new PreviewColumnHeader { Title = "Source File", ColumnKey = PreviewColumnKey.SourceFile });
        viewModel.SortActivePreviewColumn(ListSortDirection.Ascending);
        Assert.Equal("a.txt", viewModel.NewFiles[0].RelativePath);
        Assert.Equal("b.txt", viewModel.NewFiles[1].RelativePath);

        viewModel.SortActivePreviewColumn(ListSortDirection.Descending);
        Assert.Equal("b.txt", viewModel.NewFiles[0].RelativePath);
        Assert.Equal("a.txt", viewModel.NewFiles[1].RelativePath);
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
    public async Task ToggleSyncCommand_MentionsChecksumVerification_WhenMoveModeIsEnabled()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("moved.txt", "payload");
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
            MoveMode = true,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);
        viewModel.AreAllNewFilesSelected = true;

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() =>
            File.Exists(Path.Combine(workspace.DestinationPath, "moved.txt")) &&
            !File.Exists(Path.Combine(workspace.SourcePath, "moved.txt")) &&
            viewModel.StatusMessage == "Synchronization complete. Applied 1 action(s). Checksum verification passed for 1 copied file(s)." &&
            !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.Equal("Synchronization complete. Applied 1 action(s). Checksum verification passed for 1 copied file(s).", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleSyncCommand_PassesMoveModeToExecutionClient()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("queued.txt", "payload");
        var executionClient = new StubSyncExecutionClient();
        using var viewModel = new MainWindowViewModel(
            new SyncService(),
            settingsStore: null,
            folderPickerService: new StubFolderPickerService(null),
            syncExecutionClient: executionClient)
        {
            SourcePath = workspace.SourcePath,
            DestinationPath = workspace.DestinationPath,
            DryRun = false,
            MoveMode = true,
        };

        viewModel.AnalyzeCommand.Execute(null);
        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);
        viewModel.AreAllNewFilesSelected = true;

        viewModel.ToggleSyncCommand.Execute(null);
        await WaitForAsync(() => executionClient.InvocationCount == 1 && !viewModel.IsSyncRunning).ConfigureAwait(true);

        Assert.True(executionClient.LastConfiguration!.MoveMode);
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
    public async Task AnalyzeCommand_ShowsValidationError_WhenDestinationDriveIsUnavailable()
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

        await WaitForAsync(() => viewModel.NewFilesCount == 1 && viewModel.AllFilesCount == 1).ConfigureAwait(true);

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
        var nestedRelativePath = Path.Combine("nested", "two.txt");
        await WaitForAsync(() =>
            viewModel.NewFiles.Any(row => RelativePathEquals(row.RelativePath, "one.txt")) &&
            viewModel.NewFiles.Any(row => RelativePathEquals(row.RelativePath, nestedRelativePath))).ConfigureAwait(true);

        viewModel.SelectAllInTab(PreviewTabKind.NewFiles);

        Assert.All(viewModel.NewFiles.Where(row => row.CanSelect), row => Assert.True(row.IsSelected));
        Assert.Equal(viewModel.NewFiles.Count(row => row.CanSelect), viewModel.RemainingQueue.Count);
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
        var holidayRelativePath = Path.Combine("photos", "holiday-shot.txt");
        var notesRelativePath = Path.Combine("docs", "notes.txt");
        await WaitForAsync(() =>
            viewModel.NewFiles.Any(row => RelativePathEquals(row.RelativePath, holidayRelativePath)) &&
            viewModel.NewFiles.Any(row => RelativePathEquals(row.RelativePath, notesRelativePath))).ConfigureAwait(true);

        var fileNameMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "holiday", PreviewSelectionTarget.FileName);
        Assert.Equal(1, fileNameMatches);
        Assert.True(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, holidayRelativePath)).IsSelected);
        Assert.False(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, notesRelativePath)).IsSelected);

        var folderMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "docs", PreviewSelectionTarget.FileFolder);
        Assert.Equal(1, folderMatches);
        Assert.False(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, holidayRelativePath)).IsSelected);
        Assert.True(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, notesRelativePath)).IsSelected);

        var fullPathMatches = viewModel.SelectByPattern(PreviewTabKind.NewFiles, "photos\\holiday-shot", PreviewSelectionTarget.FullPath);
        Assert.Equal(1, fullPathMatches);
        Assert.True(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, holidayRelativePath)).IsSelected);
        Assert.False(viewModel.NewFiles.Single(row => RelativePathEquals(row.RelativePath, notesRelativePath)).IsSelected);
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

    [Fact]
    public void FileComparisonPaneViewModel_Create_UsesEmptyPreviewWhenPathMissing()
    {
        var pane = FileComparisonPaneViewModel.Create("Destination", string.Empty, string.Empty, string.Empty, new FilePreviewService());

        Assert.Equal(string.Empty, pane.FileName);
        Assert.False(pane.HasFile);
        Assert.False(pane.HasPath);
        Assert.True(pane.HasTextPreview);
        Assert.Equal("No File", pane.PreviewText);
        Assert.False(pane.HasImagePreview);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForTextFiles()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("preview.txt", "hello comparison");

        var pane = FileComparisonPaneViewModel.Create("Source", Path.Combine(workspace.SourcePath, "preview.txt"), "16 B", "2026-03-11 10:00:00", new FilePreviewService());

        Assert.True(pane.HasPath);
        Assert.True(pane.HasTextPreview);
        Assert.Contains("hello comparison", pane.PreviewText);
        Assert.False(pane.HasImagePreview);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_UsesMessageForUnsupportedFileTypes()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("archive.bin", "binary-ish");

        var pane = FileComparisonPaneViewModel.Create("Source", Path.Combine(workspace.SourcePath, "archive.bin"), "10 B", "2026-03-11 10:00:00", new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.Equal("Preview for item type not supported", pane.PreviewText);
        Assert.False(pane.HasImagePreview);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_UsesPdfPreviewForPdfFiles()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("guide.pdf", "placeholder");

        var pane = FileComparisonPaneViewModel.Create("Source", Path.Combine(workspace.SourcePath, "guide.pdf"), "1 KB", "2026-03-11 10:00:00", new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.True(pane.HasPdfPreview);
        Assert.False(pane.HasMediaPreview);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_UsesMediaPreviewForMediaFiles()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("clip.mp4", "placeholder");

        var pane = FileComparisonPaneViewModel.Create("Source", Path.Combine(workspace.SourcePath, "clip.mp4"), "1 KB", "2026-03-11 10:00:00", new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.True(pane.HasMediaPreview);
        Assert.False(pane.HasPdfPreview);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForOfficeFiles()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateDocx(
            Path.Combine(workspace.SourcePath, "plan.docx"),
            "Quarterly Plan",
            "Milestone A");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "plan.docx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: false)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Quarterly Plan", pane.PreviewText);
        Assert.Contains("Milestone A", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForOfficeFilesEvenWhenHandlerExists()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateDocx(
            Path.Combine(workspace.SourcePath, "plan.docx"),
            "Quarterly Plan",
            "Milestone A");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "plan.docx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: true)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Quarterly Plan", pane.PreviewText);
        Assert.Contains("Milestone A", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForExcelFiles()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateXlsx(
            Path.Combine(workspace.SourcePath, "budget.xlsx"),
            "Budget",
            ["Category", "Amount"],
            ["Travel", "1500"]);

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "budget.xlsx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: false)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Budget", pane.PreviewText);
        Assert.Contains("Category", pane.PreviewText);
        Assert.Contains("Travel", pane.PreviewText);
        Assert.Contains("1500", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_DoesNotUseShellPreviewForExcelFilesEvenWhenHandlerExists()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateXlsx(
            Path.Combine(workspace.SourcePath, "budget.xlsx"),
            "Budget",
            ["Category", "Amount"],
            ["Travel", "1500"]);

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "budget.xlsx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: true)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Budget", pane.PreviewText);
        Assert.Contains("Category", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForArchiveOnlyExcelFiles()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateArchiveOnlyXlsx(
            Path.Combine(workspace.SourcePath, "archive-only.xlsx"),
            "Budget",
            ["Category", "Amount"],
            ["Travel", "1500"]);

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "archive-only.xlsx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: false)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Budget", pane.PreviewText);
        Assert.Contains("Category", pane.PreviewText);
        Assert.Contains("Travel", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_ShowsExcelDiagnosticsWhenPreviewExtractionFails()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("broken.xlsx", "not an excel workbook");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "broken.xlsx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: false)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.Contains("Excel preview diagnostics", pane.PreviewText);
        Assert.Contains("does not have a valid ZIP-based Excel workbook signature", pane.PreviewText);
        Assert.Contains("ExcelDataReader", pane.PreviewText);
        Assert.Contains("Open XML SDK", pane.PreviewText);
        Assert.Contains("Archive XML fallback", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_ShowsWordDiagnosticsWhenPreviewExtractionFails()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("broken.docx", "not a word document");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "broken.docx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService(shellPreviewHandlerResolver: new StubShellPreviewHandlerResolver(isAvailable: true)));

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.False(pane.HasShellPreview);
        Assert.Contains("Word preview diagnostics", pane.PreviewText);
        Assert.Contains("does not have a valid ZIP-based Word document signature", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_IsOfficeFile_TrueForDocx()
    {
        using var workspace = new SyncTestWorkspace();
        OfficePreviewTestFileFactory.CreateDocx(
            Path.Combine(workspace.SourcePath, "test.docx"),
            "Hello");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "test.docx"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService());

        Assert.True(pane.IsOfficeFile);
    }

    [Fact]
    public void FileComparisonPaneViewModel_IsOfficeFile_FalseForTextFiles()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("readme.txt", "hello world");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            Path.Combine(workspace.SourcePath, "readme.txt"),
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService());

        Assert.False(pane.IsOfficeFile);
    }

    [Fact]
    public void FileComparisonPaneViewModel_IsOfficeFile_FalseWhenFileDoesNotExist()
    {
        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            @"C:\no\such\file.docx",
            "1 KB",
            "2026-03-11 10:00:00",
            new FilePreviewService());

        Assert.False(pane.IsOfficeFile);
    }

    [Fact]
    public void ExtractPreviewWithMode_OpenXml_ExtractsDocxContent()
    {
        using var workspace = new SyncTestWorkspace();
        var path = Path.Combine(workspace.SourcePath, "plan.docx");
        OfficePreviewTestFileFactory.CreateDocx(path, "Budget Report", "Summary");

        var result = OfficePreviewExtractor.ExtractPreviewWithMode(path, 8000, OfficePreviewMode.OpenXml);

        Assert.True(result.HasPreview);
        Assert.Contains("Budget Report", result.PreviewText);
    }

    [Fact]
    public void ExtractPreviewWithMode_OpenXml_ExtractsXlsxContent()
    {
        using var workspace = new SyncTestWorkspace();
        var path = Path.Combine(workspace.SourcePath, "data.xlsx");
        OfficePreviewTestFileFactory.CreateXlsx(path, "Sheet1", new[] { new[] { "A1", "B1" }, new[] { "A2", "B2" } });

        var result = OfficePreviewExtractor.ExtractPreviewWithMode(path, 8000, OfficePreviewMode.OpenXml);

        Assert.True(result.HasPreview);
        Assert.Contains("A1", result.PreviewText);
    }

    [Fact]
    public void ExtractPreviewWithMode_OpenXml_ReturnsDiagnosticsForCorruptFile()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("corrupt.docx", "not valid");

        var result = OfficePreviewExtractor.ExtractPreviewWithMode(
            Path.Combine(workspace.SourcePath, "corrupt.docx"), 8000, OfficePreviewMode.OpenXml);

        Assert.False(result.HasPreview);
        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosticText));
    }

    [Fact]
    public void ExtractPreviewWithMode_OfficeInterop_ReturnsDiagnosticsForCorruptFile()
    {
        using var workspace = new SyncTestWorkspace();
        workspace.WriteSourceFile("corrupt.docx", "not valid");

        var result = OfficePreviewExtractor.ExtractPreviewWithMode(
            Path.Combine(workspace.SourcePath, "corrupt.docx"), 8000, OfficePreviewMode.OfficeInterop);

        Assert.False(result.HasPreview);
        Assert.False(string.IsNullOrWhiteSpace(result.DiagnosticText));
    }

    [Fact]
    public void ExtractPreviewWithMode_Shell_ReturnsDiagnosticMessage()
    {
        using var workspace = new SyncTestWorkspace();
        var path = Path.Combine(workspace.SourcePath, "plan.docx");
        OfficePreviewTestFileFactory.CreateDocx(path, "Hello");

        var result = OfficePreviewExtractor.ExtractPreviewWithMode(path, 8000, OfficePreviewMode.Shell);

        Assert.False(result.HasPreview);
        Assert.Contains("shell preview", result.DiagnosticText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForUploadedWordAsset()
    {
        var assetPath = GetTestAssetPath("*.docx");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            assetPath,
            "1 KB",
            "2026-03-12 10:00:00",
            new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.Contains("Word Unit Test", pane.PreviewText);
        Assert.Contains("This is a test", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForUploadedExcelAsset()
    {
        var assetPath = GetTestAssetPath("*.xlsx");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            assetPath,
            "1 KB",
            "2026-03-12 10:00:00",
            new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.Contains("Pivot", pane.PreviewText);
        Assert.Contains("Column1", pane.PreviewText);
        Assert.Contains("Grand Total", pane.PreviewText);
        Assert.Contains("ColumnA", pane.PreviewText);
    }

    [Fact]
    public void FileComparisonPaneViewModel_Create_LoadsTextPreviewForUploadedPowerPointAsset()
    {
        var assetPath = GetTestAssetPath("*.pptx");

        var pane = FileComparisonPaneViewModel.Create(
            "Source",
            assetPath,
            "1 KB",
            "2026-03-12 10:00:00",
            new FilePreviewService());

        Assert.True(pane.HasFile);
        Assert.True(pane.HasTextPreview);
        Assert.Contains("Slide 1", pane.PreviewText);
        Assert.Contains("Analyze", pane.PreviewText);
        Assert.Contains("OVERWRITE", pane.PreviewText);
        Assert.Contains("DELETE", pane.PreviewText);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not met within the allowed time.");
            }

            await Task.Delay(25).ConfigureAwait(true);
        }
    }

    private static string GetTestAssetPath(string fileNameOrPattern)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var directCandidate = FindAsset(directory.FullName, fileNameOrPattern);
            if (directCandidate is not null)
            {
                return directCandidate;
            }

            var nestedCandidate = FindAsset(Path.Combine(directory.FullName, "UsbFileSync.Tests"), fileNameOrPattern);
            if (nestedCandidate is not null)
            {
                return nestedCandidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate test asset '{fileNameOrPattern}' from '{AppContext.BaseDirectory}'.");
    }

    private static string? FindAsset(string rootDirectory, string fileNameOrPattern)
    {
        var assetDirectory = Path.Combine(rootDirectory, "TestAssets");
        if (!Directory.Exists(assetDirectory))
        {
            return null;
        }

        var exactCandidate = Path.Combine(assetDirectory, fileNameOrPattern);
        if (!fileNameOrPattern.Contains('*') && !fileNameOrPattern.Contains('?'))
        {
            return File.Exists(exactCandidate) ? exactCandidate : null;
        }

        return Directory.GetFiles(assetDirectory, fileNameOrPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private sealed class StubFolderPickerService(string? selectedPath) : IFolderPickerService
    {
        public string? PickFolder(string title, string? initialPath) => selectedPath;
    }

    private sealed class StubSourceVolumeService(IVolumeSource? volume, string? failureReason = null) : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out IVolumeSource? resolvedVolume, out string? resolvedFailureReason)
        {
            resolvedVolume = volume;
            resolvedFailureReason = volume is null ? failureReason : null;
            return volume is not null;
        }
    }

    private sealed class StubSyncExecutionClient : ISyncExecutionClient
    {
        public int InvocationCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public IReadOnlyList<SyncAction> LastActions { get; private set; } = Array.Empty<SyncAction>();

        public SyncConfiguration? LastConfiguration { get; private set; }

        public Task<SyncResult> ExecuteAsync(
            SyncConfiguration configuration,
            IReadOnlyList<SyncAction> actions,
            IProgress<SyncProgress>? progress,
            IProgress<int>? autoParallelism,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastConfiguration = configuration;
            LastCancellationToken = cancellationToken;
            LastActions = actions.ToList();
            return Task.FromResult(new SyncResult(actions.ToList(), actions.Count, false));
        }
    }

    private sealed class BlockingSyncExecutionClient : ISyncExecutionClient
    {
        private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Started { get; private set; }

        public bool CancelObserved { get; private set; }

        public async Task<SyncResult> ExecuteAsync(
            SyncConfiguration configuration,
            IReadOnlyList<SyncAction> actions,
            IProgress<SyncProgress>? progress,
            IProgress<int>? autoParallelism,
            CancellationToken cancellationToken)
        {
            Started = true;
            using var registration = cancellationToken.Register(() =>
            {
                CancelObserved = true;
                _completionSource.TrySetCanceled(cancellationToken);
            });

            await _completionSource.Task.ConfigureAwait(false);
            return new SyncResult(actions.ToList(), actions.Count, false);
        }
    }

    private sealed class StubVolumeSource : IVolumeSource
    {
        private readonly string _backingRoot;
        private readonly string _root;

        public StubVolumeSource(string root, string backingRoot, bool isReadOnly, string fileSystemType)
        {
            _root = root;
            _backingRoot = backingRoot;
            Id = $"stub::{root}";
            DisplayName = root;
            FileSystemType = fileSystemType;
            IsReadOnly = isReadOnly;
            Root = root;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string FileSystemType { get; }

        public bool IsReadOnly { get; }

        public string Root { get; }

        public IFileEntry GetEntry(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            var backingPath = ToBackingPath(normalizedRelativePath);
            var displayPath = ToDisplayPath(normalizedRelativePath);
            var name = string.IsNullOrEmpty(normalizedRelativePath)
                ? _root
                : Path.GetFileName(normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(backingPath))
            {
                var directoryInfo = new DirectoryInfo(backingPath);
                return new StubFileEntry(displayPath, name, true, null, directoryInfo.LastWriteTimeUtc, true);
            }

            if (File.Exists(backingPath))
            {
                var fileInfo = new FileInfo(backingPath);
                return new StubFileEntry(displayPath, name, false, fileInfo.Length, fileInfo.LastWriteTimeUtc, true);
            }

            return new StubFileEntry(displayPath, name, false, null, null, false);
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            var directoryPath = ToBackingPath(normalizedRelativePath);
            if (!Directory.Exists(directoryPath))
            {
                return Array.Empty<IFileEntry>();
            }

            return Directory.EnumerateFileSystemEntries(directoryPath)
                .Select(entryPath =>
                {
                    var relativePath = Path.GetRelativePath(_backingRoot, entryPath)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    return GetEntry(relativePath);
                })
                .ToArray();
        }

        public Stream OpenRead(string path)
        {
            var entry = GetEntry(path);
            if (!entry.Exists || entry.IsDirectory)
            {
                throw new FileNotFoundException($"The file '{entry.FullPath}' does not exist.", entry.FullPath);
            }

            return new FileStream(ToBackingPath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public Stream OpenWrite(string path, bool overwrite = true)
        {
            EnsureWritable();
            var backingPath = ToBackingPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(backingPath)!);
            return new FileStream(
                backingPath,
                overwrite ? FileMode.Create : FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
        }

        public void CreateDirectory(string path)
        {
            EnsureWritable();
            Directory.CreateDirectory(ToBackingPath(path));
        }

        public void DeleteFile(string path)
        {
            EnsureWritable();
            var backingPath = ToBackingPath(path);
            if (File.Exists(backingPath))
            {
                File.Delete(backingPath);
            }
        }

        public void DeleteDirectory(string path)
        {
            EnsureWritable();
            var backingPath = ToBackingPath(path);
            if (Directory.Exists(backingPath))
            {
                Directory.Delete(backingPath, recursive: true);
            }
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite = false)
        {
            EnsureWritable();
            var sourceBackingPath = ToBackingPath(sourcePath);
            var destinationBackingPath = ToBackingPath(destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationBackingPath)!);

            if (File.Exists(sourceBackingPath))
            {
                if (overwrite && File.Exists(destinationBackingPath))
                {
                    File.Delete(destinationBackingPath);
                }

                File.Move(sourceBackingPath, destinationBackingPath, overwrite);
                return;
            }

            if (Directory.Exists(sourceBackingPath))
            {
                if (overwrite && Directory.Exists(destinationBackingPath))
                {
                    Directory.Delete(destinationBackingPath, recursive: true);
                }

                Directory.Move(sourceBackingPath, destinationBackingPath);
            }
        }

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            EnsureWritable();
            var backingPath = ToBackingPath(path);
            if (File.Exists(backingPath))
            {
                File.SetLastWriteTimeUtc(backingPath, lastWriteTimeUtc);
            }
            else if (Directory.Exists(backingPath))
            {
                Directory.SetLastWriteTimeUtc(backingPath, lastWriteTimeUtc);
            }
        }

        private string ToBackingPath(string? relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return _backingRoot;
            }

            return Path.Combine(_backingRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private string ToDisplayPath(string? relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return _root;
            }

            return Path.Combine(_root, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private string NormalizeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            var normalizedPath = relativePath.Replace('\\', '/');
            var normalizedRoot = _root.Replace('\\', '/').TrimEnd('/');

            if (string.Equals(normalizedPath.TrimEnd('/'), normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath[(normalizedRoot.Length + 1)..];
            }

            return normalizedPath.Trim('/');
        }

        private void EnsureWritable()
        {
            if (IsReadOnly)
            {
                throw new ReadOnlyVolumeException(DisplayName);
            }
        }

        private sealed record StubFileEntry(
            string FullPath,
            string Name,
            bool IsDirectory,
            long? Size,
            DateTime? LastWriteTimeUtc,
            bool Exists) : IFileEntry;
    }

    private sealed class StubShellPreviewHandlerResolver(bool isAvailable) : IShellPreviewHandlerResolver
    {
        public bool TryGetPreviewHandlerClsid(string filePath, out Guid previewHandlerClsid)
        {
            previewHandlerClsid = isAvailable ? Guid.NewGuid() : Guid.Empty;
            return isAvailable;
        }
    }

    private static class OfficePreviewTestFileFactory
    {
        public static void CreateDocx(string path, params string[] paragraphs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    paragraphs.Select(paragraph => new Paragraph(new WordRun(new WordText(paragraph))))));
            mainPart.Document.Save();
        }

        public static void CreateXlsx(string path, string sheetName, params string[][] rows)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            foreach (var rowValues in rows)
            {
                var row = new Row();
                foreach (var cellValue in rowValues)
                {
                    row.AppendChild(new Cell
                    {
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new SpreadsheetText(cellValue)),
                    });
                }

                sheetData.AppendChild(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = sheetName,
            });

            workbookPart.Workbook.Save();
        }

                public static void CreateArchiveOnlyXlsx(string path, string sheetName, params string[][] rows)
                {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

                        WriteEntry(
                                archive,
                                "xl/workbook.xml",
                                $$"""
                                <?xml version="1.0" encoding="UTF-8"?>
                                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                                    <sheets>
                                        <sheet name="{{System.Security.SecurityElement.Escape(sheetName)}}" sheetId="1" r:id="rId1" />
                                    </sheets>
                                </workbook>
                                """);

                        WriteEntry(
                                archive,
                                "xl/_rels/workbook.xml.rels",
                                """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                                    <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
                                </Relationships>
                                """);

                        var rowXml = string.Join(
                                Environment.NewLine,
                                rows.Select(rowValues =>
                                {
                                        var cellXml = string.Join(string.Empty, rowValues.Select(value =>
                                                $"<c t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>"));
                                        return $"<row>{cellXml}</row>";
                                }));

                        WriteEntry(
                                archive,
                                "xl/worksheets/sheet1.xml",
                                $$"""
                                <?xml version="1.0" encoding="UTF-8"?>
                                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                                    <sheetData>
                                {{rowXml}}
                                    </sheetData>
                                </worksheet>
                                """);
                }

                private static void WriteEntry(ZipArchive archive, string entryName, string contents)
                {
                        var entry = archive.CreateEntry(entryName);
                        using var writer = new StreamWriter(entry.Open());
                        writer.Write(contents);
                }
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
            var drivePath when drivePath.EndsWith("destination", StringComparison.OrdinalIgnoreCase) => $"Drive {Path.GetFileName(drivePath)}",
            var drivePath when drivePath.EndsWith("destination-two", StringComparison.OrdinalIgnoreCase) => $"Drive {Path.GetFileName(drivePath)}",
            _ => path,
        };

        public string FormatDestinationPathForDisplay(string path) => path;
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

    private static bool RelativePathEquals(string actualPath, string expectedPath) =>
        string.Equals(
            actualPath.Replace('\\', '/'),
            expectedPath.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

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

        public string CreateAdditionalDestination(string name)
        {
            var path = Path.Combine(_rootPath, name);
            Directory.CreateDirectory(path);
            return path;
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

    private sealed class IgnoreCancellationBlockingSyncStrategy(TaskCompletionSource completionSource) : ISyncStrategy
    {
        public async Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
        {
            await completionSource.Task.ConfigureAwait(false);
            return [];
        }
    }
}
