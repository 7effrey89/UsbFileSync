namespace UsbFileSync.Core.Models;

public sealed record SyncAction(
    SyncActionType Type,
    string RelativePath,
    string? SourceFullPath = null,
    string? DestinationFullPath = null,
    string? PreviousRelativePath = null);
