namespace UsbFileSync.Core.Models;

public sealed record SyncAction(
    SyncActionType Type,
    string RelativePath,
    string? SourceFullPath = null,
    string? DestinationFullPath = null,
    string? PreviousRelativePath = null)
{
    public string GetActionKey() =>
        string.Join("|", new[]
        {
            Type.ToString(),
            RelativePath,
            SourceFullPath ?? string.Empty,
            DestinationFullPath ?? string.Empty,
            PreviousRelativePath ?? string.Empty,
        });
}
