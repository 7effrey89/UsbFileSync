namespace UsbFileSync.Core.Models;

public sealed class CloudProviderAppRegistration
{
    public CloudStorageProvider Provider { get; init; } = CloudStorageProvider.GoogleDrive;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;
}
