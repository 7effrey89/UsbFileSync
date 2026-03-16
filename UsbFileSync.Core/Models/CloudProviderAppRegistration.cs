namespace UsbFileSync.Core.Models;

public sealed class CloudProviderAppRegistration
{
    public string RegistrationId { get; init; } = Guid.NewGuid().ToString("N");

    public CloudStorageProvider Provider { get; init; } = CloudStorageProvider.GoogleDrive;

    public string Alias { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;
}
