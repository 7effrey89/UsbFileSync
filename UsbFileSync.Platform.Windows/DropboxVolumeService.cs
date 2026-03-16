using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class DropboxVolumeService : ISourceVolumeService, ICloudRootProvider
{
    private readonly bool _allowWriteAccess;
    private readonly IReadOnlyList<CloudProviderAppRegistration> _registrations;

    public DropboxVolumeService(bool useCustomCloudProviderCredentials, IReadOnlyList<CloudProviderAppRegistration>? registrations, bool allowWriteAccess = false)
    {
        _allowWriteAccess = allowWriteAccess;
        _registrations = useCustomCloudProviderCredentials
            ? (registrations ?? Array.Empty<CloudProviderAppRegistration>())
                .Where(item => item.Provider == CloudStorageProvider.Dropbox && !string.IsNullOrWhiteSpace(item.ClientId))
                .ToArray()
            : Array.Empty<CloudProviderAppRegistration>();
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!DropboxPath.TryParse(path, out var registrationId, out var relativePath))
        {
            return false;
        }

        var registration = ResolveRegistration(registrationId);
        if (registration is null || string.IsNullOrWhiteSpace(registration.ClientId))
        {
            failureReason = "Dropbox custom credentials are not configured. Enter a Dropbox app key in Application Settings and enable custom provider credentials.";
            return false;
        }

        try
        {
            var authenticationService = new DropboxAuthenticationService(registration.ClientId, registration.ClientSecret, tokenCacheKey: registration.RegistrationId);
            var apiClient = new DropboxApiClient(authenticationService);
            var rootVolume = new DropboxVolumeSource(apiClient, registration.RegistrationId, BuildDisplayName(registration), _allowWriteAccess);

            _ = rootVolume.GetEntry(string.Empty);

            volume = string.IsNullOrEmpty(relativePath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativePath);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            failureReason = $"The Dropbox folder '{path}' was not found.";
            return false;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            return false;
        }
    }

    public IReadOnlyList<CloudRootDefinition> GetAvailableRoots() =>
        _registrations
            .Select(registration => new CloudRootDefinition(
                DropboxPath.BuildRootPath(registration.RegistrationId),
                BuildDisplayName(registration),
                CreateRootVolume(registration)))
            .ToArray();

    private CloudProviderAppRegistration? ResolveRegistration(string? registrationId)
    {
        if (!string.IsNullOrWhiteSpace(registrationId))
        {
            return _registrations.FirstOrDefault(item => string.Equals(item.RegistrationId, registrationId, StringComparison.OrdinalIgnoreCase));
        }

        return _registrations.LastOrDefault();
    }

    private DropboxVolumeSource CreateRootVolume(CloudProviderAppRegistration registration)
    {
        var authenticationService = new DropboxAuthenticationService(registration.ClientId, registration.ClientSecret, tokenCacheKey: registration.RegistrationId);
        var apiClient = new DropboxApiClient(authenticationService);
        return new DropboxVolumeSource(apiClient, registration.RegistrationId, BuildDisplayName(registration), _allowWriteAccess);
    }

    private static string BuildDisplayName(CloudProviderAppRegistration registration) =>
        string.IsNullOrWhiteSpace(registration.Alias)
            ? "Dropbox"
            : $"Dropbox - {registration.Alias.Trim()}";
}