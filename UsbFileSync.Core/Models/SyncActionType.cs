namespace UsbFileSync.Core.Models;

public enum SyncActionType
{
    CreateDirectoryOnDestination,
    CreateDirectoryOnSource,
    CopyToDestination,
    CopyToSource,
    OverwriteFileOnDestination,
    OverwriteFileOnSource,
    DeleteDirectoryFromDestination,
    DeleteDirectoryFromSource,
    DeleteFromDestination,
    DeleteFromSource,
    MoveOnDestination,
    NoOp,
}
