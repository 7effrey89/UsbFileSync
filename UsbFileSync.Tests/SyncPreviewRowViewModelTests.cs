using System.Windows.Media;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Tests;

public sealed class SyncPreviewRowViewModelTests
{
    [Fact]
    public void UsesSourcePathAsDisplayedNameWhenAvailable()
    {
        var iconProvider = new StubFileIconProvider(null);
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "folder/file.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\folder\file.txt",
            SourceLength: 10,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\folder\file.txt",
            DestinationLength: 10,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "->",
            Status: "New File",
            Category: SyncPreviewCategory.NewFiles,
            PlannedActionType: SyncActionType.CopyToDestination), iconProvider);

        Assert.Equal(@"F:\folder\file.txt", row.Name);
        Assert.Equal(@"F:\folder\file.txt", iconProvider.RequestedPath);
        Assert.False(iconProvider.RequestedIsDirectory);
        Assert.Null(row.IconSource);
        Assert.Equal("\uE8A5", row.IconGlyph);
        Assert.Equal("+", row.StatusGlyph);
        Assert.Equal("ADD", row.ActionBadgeText);
        Assert.Equal("Pending", row.TransferSpeedText);
        Assert.Equal(0, row.ProgressValue);
        Assert.Equal("Queued", row.ProgressStateText);
    }

    [Fact]
    public void UsesFolderIconAndDestinationPathWhenSourcePathIsMissing()
    {
        var iconProvider = new StubFileIconProvider(new DrawingImage());
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "folder",
            IsDirectory: true,
            SourceFullPath: null,
            SourceLength: null,
            SourceLastWriteTimeUtc: null,
            DestinationFullPath: @"E:\folder",
            DestinationLength: null,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "<-",
            Status: "Deleted",
            Category: SyncPreviewCategory.DeletedFiles,
            PlannedActionType: SyncActionType.DeleteDirectoryFromDestination), iconProvider);

        Assert.Equal(@"E:\folder", row.Name);
        Assert.Equal(@"E:\folder", iconProvider.RequestedPath);
        Assert.True(iconProvider.RequestedIsDirectory);
        Assert.Same(iconProvider.IconToReturn, row.IconSource);
        Assert.Equal("\uE8B7", row.IconGlyph);
        Assert.Equal("×", row.StatusGlyph);
        Assert.Equal("DELETE", row.ActionBadgeText);
        Assert.Equal("Pending", row.TransferSpeedText);
        Assert.Equal(0, row.ProgressValue);
        Assert.Equal("Queued", row.ProgressStateText);
    }

    [Fact]
    public void UnchangedItemsStartAsCompleted()
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "same.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\same.txt",
            SourceLength: 20,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\same.txt",
            DestinationLength: 20,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "=",
            Status: "Unchanged",
            Category: SyncPreviewCategory.UnchangedFiles,
            PlannedActionType: null), new StubFileIconProvider(null));

        Assert.Equal(100, row.ProgressValue);
        Assert.Equal("▮▮", row.StatusGlyph);
        Assert.Equal("UNCHANGED", row.ActionBadgeText);
        Assert.Equal("On hold", row.TransferSpeedText);
        Assert.Equal("Done", row.ProgressStateText);
    }

    [Fact]
    public void ModifiedItemsUsePencilStatusGlyph()
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "changed.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\changed.txt",
            SourceLength: 20,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\changed.txt",
            DestinationLength: 10,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "->",
            Status: "Modified",
            Category: SyncPreviewCategory.ChangedFiles,
            PlannedActionType: SyncActionType.OverwriteFileOnDestination), new StubFileIconProvider(null));

        Assert.Equal("✎", row.StatusGlyph);
        Assert.Equal("OVERWRITE", row.ActionBadgeText);
    }

    [Fact]
    public void RenamedItemsUseMoveBadgeAndGlyph()
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "renamed.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\renamed.txt",
            SourceLength: 20,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\other-name.txt",
            DestinationLength: 20,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "->",
            Status: "Renamed",
            Category: SyncPreviewCategory.ChangedFiles,
            PlannedActionType: SyncActionType.MoveOnDestination), new StubFileIconProvider(null));

        Assert.Equal("⇄", row.StatusGlyph);
        Assert.Equal("MOVE", row.ActionBadgeText);
    }

    [Fact]
    public void ProgressMethodsUpdateDisplayedState()
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "queued.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\queued.txt",
            SourceLength: 30,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\queued.txt",
            DestinationLength: null,
            DestinationLastWriteTimeUtc: null,
            Direction: "->",
            Status: "New File",
            Category: SyncPreviewCategory.NewFiles,
            PlannedActionType: SyncActionType.CopyToDestination), new StubFileIconProvider(null));

        row.MarkInProgress(42, 2048, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(42, row.ProgressValue);
        Assert.Equal("Transferring", row.ProgressStateText);
        Assert.Equal("42%", row.ProgressText);
        Assert.EndsWith("/s", row.TransferSpeedText);

        row.MarkPaused();
        Assert.Equal("Paused", row.ProgressStateText);
        Assert.Equal("Paused", row.TransferSpeedText);

        row.MarkCompleted();
        Assert.Equal(100, row.ProgressValue);
        Assert.Equal("Done", row.ProgressStateText);
        Assert.Equal("Done", row.TransferSpeedText);
    }

    private sealed class StubFileIconProvider(ImageSource? iconToReturn) : IFileIconProvider
    {
        public string RequestedPath { get; private set; } = string.Empty;

        public bool RequestedIsDirectory { get; private set; }

        public ImageSource? IconToReturn { get; } = iconToReturn;

        public ImageSource? GetIcon(string path, bool isDirectory)
        {
            RequestedPath = path;
            RequestedIsDirectory = isDirectory;
            return IconToReturn;
        }
    }
}
