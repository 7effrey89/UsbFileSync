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

    public static string GetSlug(CloudStorageProvider provider) => provider switch
    {
        CloudStorageProvider.GoogleDrive => "googledrive",
        CloudStorageProvider.Dropbox => "dropbox",
        CloudStorageProvider.OneDrive => "onedrive",
        _ => provider.ToString().ToLowerInvariant(),
    };

    public static bool TryParseSlug(string? slug, out CloudStorageProvider provider)
    {
        switch (slug?.Trim().ToLowerInvariant())
        {
            case "googledrive":
                provider = CloudStorageProvider.GoogleDrive;
                return true;
            case "dropbox":
                provider = CloudStorageProvider.Dropbox;
                return true;
            case "onedrive":
                provider = CloudStorageProvider.OneDrive;
                return true;
            default:
                provider = default;
                return false;
        }
    }
}
