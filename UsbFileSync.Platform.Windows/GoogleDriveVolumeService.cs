using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class GoogleDriveVolumeService : ISourceVolumeService
{
    private readonly bool _allowWriteAccess;
    private readonly CloudProviderAppRegistration? _registration;

    public GoogleDriveVolumeService(bool useCustomCloudProviderCredentials, IReadOnlyList<CloudProviderAppRegistration>? registrations, bool allowWriteAccess = false)
    {
        _allowWriteAccess = allowWriteAccess;
        _registration = useCustomCloudProviderCredentials
            ? registrations?
                .Where(item => item.Provider == CloudStorageProvider.GoogleDrive)
                .LastOrDefault(item => !string.IsNullOrWhiteSpace(item.ClientId))
            : null;
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!GoogleDrivePath.TryParse(path, out var relativePath))
        {
            return false;
        }

        if (_registration is null || string.IsNullOrWhiteSpace(_registration.ClientId))
        {
            failureReason = "Google Drive custom credentials are not configured. Enter a Google OAuth client ID in Application Settings and enable custom provider credentials.";
            return false;
        }

        try
        {
            var authenticationService = new GoogleDriveAuthenticationService(_registration.ClientId, _registration.ClientSecret);
            var apiClient = new GoogleDriveApiClient(authenticationService);
            var rootVolume = new GoogleDriveVolumeSource(apiClient, _allowWriteAccess);

            _ = rootVolume.GetEntry(string.Empty);

            volume = string.IsNullOrEmpty(relativePath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativePath);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            failureReason = $"The Google Drive folder '{path}' was not found.";
            return false;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            return false;
        }
    }
}