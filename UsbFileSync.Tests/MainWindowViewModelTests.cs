using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Strategies;

namespace UsbFileSync.Tests;

public sealed class MainWindowViewModelTests
{
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
        using var viewModel = new MainWindowViewModel(syncService, settingsStore: null, folderPickerService: new StubFolderPickerService(null))
        {
            SourcePath = "F:\\Primary",
            DestinationPath = "E:\\Backup"
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
    public void UpdateParallelCopyCount_AllowsUnlimitedValue()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.UpdateParallelCopyCount(0);

        Assert.Equal(0, viewModel.ParallelCopyCount);
    }

    [Fact]
    public void VerifyChecksums_CanBeEnabled()
    {
        using var viewModel = new MainWindowViewModel(new SyncService(), settingsStore: null, folderPickerService: new StubFolderPickerService(null));

        viewModel.VerifyChecksums = true;

        Assert.True(viewModel.VerifyChecksums);
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