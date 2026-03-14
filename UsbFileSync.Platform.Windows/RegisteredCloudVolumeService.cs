using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class RegisteredCloudVolumeService : ISourceVolumeService
{
    private readonly ICloudAccountStore _cloudAccountStore;

    public RegisteredCloudVolumeService(ICloudAccountStore? cloudAccountStore = null)
    {
        _cloudAccountStore = cloudAccountStore ?? CloudAccountStoreFactory.CreateDefault();
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!CloudPath.TryParse(path, out var provider, out var accountId, out var relativePath))
        {
            return false;
        }

        var account = _cloudAccountStore.Load()
            .FirstOrDefault(candidate =>
                candidate.Provider == provider &&
                string.Equals(candidate.Id, accountId, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            failureReason = $"The selected {CloudStorageProviderInfo.GetDisplayName(provider)} account is no longer registered.";
            return false;
        }

        var localRootPath = account.LocalRootPath;
        if (string.IsNullOrWhiteSpace(localRootPath) || !Directory.Exists(localRootPath))
        {
            failureReason = $"The linked {CloudStorageProviderInfo.GetDisplayName(provider)} folder '{localRootPath}' is not available.";
            return false;
        }

        var targetDirectoryPath = string.IsNullOrEmpty(relativePath)
            ? localRootPath
            : Path.Combine(localRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(targetDirectoryPath))
        {
            failureReason = $"The selected {CloudStorageProviderInfo.GetDisplayName(provider)} folder '{path}' does not exist.";
            return false;
        }

        volume = new RegisteredCloudVolumeSource(account, relativePath);
        return true;
    }
}
