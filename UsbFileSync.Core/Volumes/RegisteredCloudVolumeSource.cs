using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Volumes;

public sealed class RegisteredCloudVolumeSource : DirectoryBackedVolumeSource
{
    public RegisteredCloudVolumeSource(CloudAccountRegistration account, string? relativeRootPath = null)
        : base(
            id: BuildId(account, relativeRootPath),
            displayName: BuildDisplayName(account, relativeRootPath),
            fileSystemType: CloudStorageProviderInfo.GetDisplayName(account.Provider),
            isReadOnly: false,
            root: BuildRoot(account, relativeRootPath),
            backingRoot: BuildBackingRoot(account, relativeRootPath),
            useWindowsDisplayPaths: false)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
    }

    public CloudAccountRegistration Account { get; }

    private static string BuildId(CloudAccountRegistration account, string? relativeRootPath)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativeRootPath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? $"cloud::{CloudStorageProviderInfo.GetSlug(account.Provider)}::{account.Id}"
            : $"cloud::{CloudStorageProviderInfo.GetSlug(account.Provider)}::{account.Id}::{normalizedRelativePath}";
    }

    private static string BuildDisplayName(CloudAccountRegistration account, string? relativeRootPath)
    {
        var providerDisplayName = CloudStorageProviderInfo.GetDisplayName(account.Provider);
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativeRootPath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? $"{providerDisplayName} ({account.Login})"
            : $"{providerDisplayName} ({account.Login}) / {normalizedRelativePath}";
    }

    private static string BuildRoot(CloudAccountRegistration account, string? relativeRootPath)
    {
        var accountRoot = CloudPath.CreateRoot(account.Provider, account.Id);
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativeRootPath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? accountRoot
            : $"{accountRoot}/{normalizedRelativePath}";
    }

    private static string BuildBackingRoot(CloudAccountRegistration account, string? relativeRootPath)
    {
        if (string.IsNullOrWhiteSpace(account.LocalRootPath))
        {
            throw new ArgumentException("A local cloud account root is required.", nameof(account));
        }

        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativeRootPath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return account.LocalRootPath;
        }

        var relativeOsPath = normalizedRelativePath
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(account.LocalRootPath, relativeOsPath);
    }
}
