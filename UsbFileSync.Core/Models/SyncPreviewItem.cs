namespace UsbFileSync.Core.Models;

public sealed record SyncPreviewItem(
    string RelativePath,
    bool IsDirectory,
    string? SourceFullPath,
    long? SourceLength,
    DateTime? SourceLastWriteTimeUtc,
    string? DestinationFullPath,
    long? DestinationLength,
    DateTime? DestinationLastWriteTimeUtc,
    string Direction,
    string Status,
    SyncPreviewCategory Category,
    SyncActionType? PlannedActionType = null);