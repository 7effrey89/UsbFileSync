using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Tests;

public sealed class QueueActionViewModelTests
{
    [Fact]
    public void FullPath_UsesDestinationPath_ForDestinationActions()
    {
        var action = new SyncAction(
            SyncActionType.CopyToDestination,
            "Movies\\clip.mkv",
            SourceFullPath: "F:\\Source\\Movies\\clip.mkv",
            DestinationFullPath: "G:\\Backup\\Movies\\clip.mkv");

        var viewModel = new QueueActionViewModel(action);

        Assert.Equal("G:\\Backup\\Movies\\clip.mkv", viewModel.FullPath);
    }

    [Fact]
    public void FullPath_UsesSourcePath_ForSourceActions()
    {
        var action = new SyncAction(
            SyncActionType.CopyToSource,
            "Movies\\clip.mkv",
            SourceFullPath: "F:\\Source\\Movies\\clip.mkv",
            DestinationFullPath: "G:\\Backup\\Movies\\clip.mkv");

        var viewModel = new QueueActionViewModel(action);

        Assert.Equal("F:\\Source\\Movies\\clip.mkv", viewModel.FullPath);
    }

    [Fact]
    public void FullPath_FallsBackToRelativePath_WhenActionHasNoFullPath()
    {
        var action = new SyncAction(
            SyncActionType.CopyToDestination,
            "Movies\\clip.mkv");

        var viewModel = new QueueActionViewModel(action);

        Assert.Equal("Movies\\clip.mkv", viewModel.FullPath);
    }
}