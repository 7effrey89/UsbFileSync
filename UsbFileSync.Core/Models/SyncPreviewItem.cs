namespace UsbFileSync.Core.Models;

public sealed record SyncPreviewItem(
    string ItemKey,
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
    SyncActionType? PlannedActionType = null)
{
    public SyncPreviewItem(
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
        SyncActionType? PlannedActionType = null)
        : this(
            string.Join("|", new[]
            {
                RelativePath,
                SourceFullPath ?? string.Empty,
                DestinationFullPath ?? string.Empty,
            }),
            RelativePath,
            IsDirectory,
            SourceFullPath,
            SourceLength,
            SourceLastWriteTimeUtc,
            DestinationFullPath,
            DestinationLength,
            DestinationLastWriteTimeUtc,
            Direction,
            Status,
            Category,
            PlannedActionType)
    {
    }
}
