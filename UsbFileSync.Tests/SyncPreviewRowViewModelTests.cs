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
        Assert.Equal(@"F:\folder\file.txt", row.OpenPath);
        Assert.Equal(@"F:\folder\file.txt", iconProvider.RequestedPath);
        Assert.False(iconProvider.RequestedIsDirectory);
        Assert.Null(row.IconSource);
        Assert.Equal("\uE8A5", row.IconGlyph);
        Assert.Equal("+", row.StatusGlyph);
        Assert.Equal("CopyToDestination", row.Action);
        Assert.Equal("Add", row.SyncActionText);
        Assert.Equal(string.Empty, row.SourceStatusGlyph);
        Assert.Equal("+", row.DestinationStatusGlyph);
        AssertBrushColor(row.SourcePathBrush, 32, 32, 32);
        AssertBrushColor(row.DestinationPathBrush, 18, 140, 68);
        Assert.Equal("Pending", row.TransferSpeedText);
        Assert.Equal(0, row.ProgressValue);
        Assert.Equal("Queued", row.ProgressStateText);
        Assert.False(row.IsSourceAction);
        Assert.True(row.IsDestinationAction);
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
        Assert.Equal(@"E:\folder", row.OpenPath);
        Assert.Equal(@"E:\folder", iconProvider.RequestedPath);
        Assert.True(iconProvider.RequestedIsDirectory);
        Assert.Same(iconProvider.IconToReturn, row.IconSource);
        Assert.Equal("\uE8B7", row.IconGlyph);
        Assert.False(row.HasSourcePath);
        Assert.True(row.HasDestinationPath);
        Assert.Equal("×", row.StatusGlyph);
        Assert.Equal("Delete", row.SyncActionText);
        Assert.Equal(string.Empty, row.SourceStatusGlyph);
        Assert.Equal("×", row.DestinationStatusGlyph);
        AssertBrushColor(row.DestinationPathBrush, 196, 43, 28);
        Assert.False(row.IsSourceAction);
        Assert.True(row.IsDestinationAction);
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
        Assert.Equal("||", row.StatusGlyph);
        Assert.Equal("Unchanged", row.SyncActionText);
        Assert.Equal("||", row.SourceStatusGlyph);
        Assert.Equal("||", row.DestinationStatusGlyph);
        AssertBrushColor(row.SourcePathBrush, 32, 32, 32);
        AssertBrushColor(row.DestinationPathBrush, 32, 32, 32);
        AssertBrushColor(row.SourceStatusGlyphBrush, 98, 98, 98);
        AssertBrushColor(row.DestinationStatusGlyphBrush, 98, 98, 98);
        AssertTransparentBrush(row.SyncActionBrush);
        AssertTransparentBrush(row.SyncActionTrackFillBrush);
        AssertTransparentBrush(row.SyncActionStrokeBrush);
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
        Assert.Equal("Overwrite", row.SyncActionText);
        Assert.Equal(string.Empty, row.SourceStatusGlyph);
        Assert.Equal("✎", row.DestinationStatusGlyph);
        AssertBrushColor(row.DestinationPathBrush, 184, 125, 0);
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
        Assert.Equal("Move", row.SyncActionText);
        Assert.Equal(string.Empty, row.SourceStatusGlyph);
        Assert.Equal("⇄", row.DestinationStatusGlyph);
        Assert.False(row.IsSourceAction);
        Assert.True(row.IsDestinationAction);
    }

    [Fact]
    public void SourceSideActionsHighlightOnlySourcePath()
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "incoming.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\incoming.txt",
            SourceLength: null,
            SourceLastWriteTimeUtc: null,
            DestinationFullPath: @"E:\incoming.txt",
            DestinationLength: 20,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "<-",
            Status: "New File",
            Category: SyncPreviewCategory.NewFiles,
            PlannedActionType: SyncActionType.CopyToSource), new StubFileIconProvider(null));

        Assert.True(row.IsSourceAction);
        Assert.False(row.IsDestinationAction);
        Assert.Equal("+", row.SourceStatusGlyph);
        Assert.Equal(string.Empty, row.DestinationStatusGlyph);
        AssertBrushColor(row.SourcePathBrush, 18, 140, 68);
        AssertBrushColor(row.DestinationPathBrush, 32, 32, 32);
    }

    [Fact]
    public void SyncActionChevronMatchesDirection()
    {
        var destinationRow = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "to-destination.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\incoming.txt",
            SourceLength: null,
            SourceLastWriteTimeUtc: null,
            DestinationFullPath: @"E:\incoming.txt",
            DestinationLength: 20,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "->",
            Status: "New File",
            Category: SyncPreviewCategory.NewFiles,
            PlannedActionType: SyncActionType.CopyToDestination), new StubFileIconProvider(null));

        Assert.False(destinationRow.IsSourceAction);
        Assert.True(destinationRow.IsDestinationAction);
        Assert.Equal("M0,2L96,2 120,14 96,26 0,26 24,14z", destinationRow.SyncActionPathData);

        var sourceRow = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "to-source.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\incoming.txt",
            SourceLength: null,
            SourceLastWriteTimeUtc: null,
            DestinationFullPath: @"E:\incoming.txt",
            DestinationLength: 20,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "<-",
            Status: "New File",
            Category: SyncPreviewCategory.NewFiles,
            PlannedActionType: SyncActionType.CopyToSource), new StubFileIconProvider(null));

        Assert.True(sourceRow.IsSourceAction);
        Assert.False(sourceRow.IsDestinationAction);
        Assert.Equal("M120,2L24,2 0,14 24,26 120,26 96,14z", sourceRow.SyncActionPathData);
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
        Assert.Equal(50.4, row.SyncActionFillWidth);

        row.MarkPaused();
        Assert.Equal("Paused", row.ProgressStateText);
        Assert.Equal("Paused", row.TransferSpeedText);
        Assert.Equal("Add", row.SyncActionText);
        Assert.Equal(50.4, row.SyncActionFillWidth);

        row.MarkCompleted();
        Assert.Equal(100, row.ProgressValue);
        Assert.Equal("Done", row.ProgressStateText);
        Assert.Equal("Done", row.TransferSpeedText);
        Assert.Equal("Added", row.SyncActionText);
        Assert.Equal(120, row.SyncActionFillWidth);
    }

    [Theory]
    [InlineData(SyncActionType.CreateDirectoryOnDestination, "Add", "Added")]
    [InlineData(SyncActionType.CreateDirectoryOnSource, "Add", "Added")]
    [InlineData(SyncActionType.CopyToDestination, "Add", "Added")]
    [InlineData(SyncActionType.CopyToSource, "Add", "Added")]
    [InlineData(SyncActionType.OverwriteFileOnDestination, "Overwrite", "Overwritten")]
    [InlineData(SyncActionType.OverwriteFileOnSource, "Overwrite", "Overwritten")]
    [InlineData(SyncActionType.DeleteDirectoryFromDestination, "Delete", "Deleted")]
    [InlineData(SyncActionType.DeleteDirectoryFromSource, "Delete", "Deleted")]
    [InlineData(SyncActionType.DeleteFromDestination, "Delete", "Deleted")]
    [InlineData(SyncActionType.DeleteFromSource, "Delete", "Deleted")]
    [InlineData(SyncActionType.MoveOnDestination, "Move", "Moved")]
    [InlineData(SyncActionType.NoOp, "Unchanged", "Unchanged")]
    public void SyncActionText_UsesExpectedPendingAndCompletedLabels(SyncActionType actionType, string pendingText, string completedText)
    {
        var row = new SyncPreviewRowViewModel(new SyncPreviewItem(
            RelativePath: "item.txt",
            IsDirectory: false,
            SourceFullPath: @"F:\item.txt",
            SourceLength: 10,
            SourceLastWriteTimeUtc: DateTime.UtcNow,
            DestinationFullPath: @"E:\item.txt",
            DestinationLength: 10,
            DestinationLastWriteTimeUtc: DateTime.UtcNow,
            Direction: "->",
            Status: actionType is SyncActionType.NoOp ? "Unchanged" : "Modified",
            Category: actionType is SyncActionType.NoOp ? SyncPreviewCategory.UnchangedFiles : SyncPreviewCategory.ChangedFiles,
            PlannedActionType: actionType), new StubFileIconProvider(null));

        Assert.Equal(pendingText, row.SyncActionText);

        row.MarkCompleted();

        Assert.Equal(completedText, row.SyncActionText);
    }

    private static void AssertBrushColor(Brush brush, byte red, byte green, byte blue)
    {
        var solidColorBrush = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Color.FromRgb(red, green, blue), solidColorBrush.Color);
    }

    private static void AssertTransparentBrush(Brush brush)
    {
        var solidColorBrush = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Color.FromArgb(0, 0, 0, 0), solidColorBrush.Color);
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
