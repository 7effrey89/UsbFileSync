using UsbFileSync.Core.Models;

namespace UsbFileSync.App.ViewModels;

public sealed class QueueActionViewModel
{
    public QueueActionViewModel(SyncAction action)
    {
        Action = action.Type.ToString();
        RelativePath = action.RelativePath;
        FullPath = ResolveFullPath(action);
    }

    public string Action { get; }

    public string RelativePath { get; }

    public string FullPath { get; }

    private static string ResolveFullPath(SyncAction action)
    {
        return action.Type switch
        {
            SyncActionType.CreateDirectoryOnDestination => action.DestinationFullPath,
            SyncActionType.CopyToDestination => action.DestinationFullPath,
            SyncActionType.OverwriteFileOnDestination => action.DestinationFullPath,
            SyncActionType.DeleteDirectoryFromDestination => action.DestinationFullPath,
            SyncActionType.DeleteFromDestination => action.DestinationFullPath,
            SyncActionType.MoveOnDestination => action.DestinationFullPath,
            SyncActionType.CreateDirectoryOnSource => action.SourceFullPath,
            SyncActionType.CopyToSource => action.SourceFullPath,
            SyncActionType.OverwriteFileOnSource => action.SourceFullPath,
            SyncActionType.DeleteDirectoryFromSource => action.SourceFullPath,
            SyncActionType.DeleteFromSource => action.SourceFullPath,
            SyncActionType.NoOp => action.SourceFullPath ?? action.DestinationFullPath,
            _ => action.SourceFullPath ?? action.DestinationFullPath,
        } ?? action.RelativePath;
    }
}