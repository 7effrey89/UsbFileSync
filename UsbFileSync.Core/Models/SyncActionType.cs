namespace UsbFileSync.Core.Models;

public enum SyncActionType
{
    CopyToDestination,
    CopyToSource,
    DeleteFromDestination,
    DeleteFromSource,
    MoveOnDestination,
    NoOp,
}
