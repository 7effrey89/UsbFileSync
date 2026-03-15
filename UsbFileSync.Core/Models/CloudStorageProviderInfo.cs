namespace UsbFileSync.Core.Models;

public static class CloudStorageProviderInfo
{
    public static string GetDisplayName(CloudStorageProvider provider) => provider switch
    {
        CloudStorageProvider.GoogleDrive => "Google Drive",
        CloudStorageProvider.Dropbox => "Dropbox",
        CloudStorageProvider.OneDrive => "OneDrive",
        _ => provider.ToString(),
    };
}
