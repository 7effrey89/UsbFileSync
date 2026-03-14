namespace UsbFileSync.Core.Models;

public sealed class CloudAccountRegistration
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public CloudStorageProvider Provider { get; init; } = CloudStorageProvider.GoogleDrive;

    public string Login { get; init; } = string.Empty;

    public string LocalRootPath { get; init; } = string.Empty;
}
