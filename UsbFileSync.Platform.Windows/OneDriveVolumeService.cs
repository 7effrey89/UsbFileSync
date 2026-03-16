using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class OneDriveVolumeService : ISourceVolumeService
{
    private readonly bool _allowWriteAccess;
    private readonly CloudProviderAppRegistration? _registration;

    public OneDriveVolumeService(bool useCustomCloudProviderCredentials, IReadOnlyList<CloudProviderAppRegistration>? registrations, bool allowWriteAccess = false)
    {
        _allowWriteAccess = allowWriteAccess;
        _registration = useCustomCloudProviderCredentials
            ? registrations?
                .Where(item => item.Provider == CloudStorageProvider.OneDrive)
                .LastOrDefault(item => !string.IsNullOrWhiteSpace(item.ClientId))
            : null;
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!OneDrivePath.TryParse(path, out var relativePath))
        {
            return false;
        }

        if (_registration is null || string.IsNullOrWhiteSpace(_registration.ClientId))
        {
            failureReason = "OneDrive custom credentials are not configured. Enter a Microsoft application client ID in Application Settings and enable custom provider credentials.";
            return false;
        }

        try
        {
            var tenantId = string.IsNullOrWhiteSpace(_registration.TenantId) ? "common" : _registration.TenantId;
            var authenticationService = new OneDriveAuthenticationService(_registration.ClientId, tenantId);
            var apiClient = new OneDriveApiClient(authenticationService);
            var rootVolume = new OneDriveVolumeSource(apiClient, _allowWriteAccess);

            _ = rootVolume.GetEntry(string.Empty);

            volume = string.IsNullOrEmpty(relativePath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativePath);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            failureReason = $"The OneDrive folder '{path}' was not found.";
            return false;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            return false;
        }
    }
}