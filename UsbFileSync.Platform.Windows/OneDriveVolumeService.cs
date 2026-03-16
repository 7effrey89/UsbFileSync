using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class OneDriveVolumeService : ISourceVolumeService, ICloudRootProvider
{
    private readonly bool _allowWriteAccess;
    private readonly IReadOnlyList<CloudProviderAppRegistration> _registrations;

    public OneDriveVolumeService(bool useCustomCloudProviderCredentials, IReadOnlyList<CloudProviderAppRegistration>? registrations, bool allowWriteAccess = false)
    {
        _allowWriteAccess = allowWriteAccess;
        _registrations = useCustomCloudProviderCredentials
            ? (registrations ?? Array.Empty<CloudProviderAppRegistration>())
                .Where(item => item.Provider == CloudStorageProvider.OneDrive && !string.IsNullOrWhiteSpace(item.ClientId))
                .ToArray()
            : Array.Empty<CloudProviderAppRegistration>();
    }

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!OneDrivePath.TryParse(path, out var registrationId, out var relativePath))
        {
            return false;
        }

        var registration = ResolveRegistration(registrationId);
        if (registration is null || string.IsNullOrWhiteSpace(registration.ClientId))
        {
            failureReason = "OneDrive custom credentials are not configured. Enter a Microsoft application client ID in Application Settings and enable custom provider credentials.";
            return false;
        }

        try
        {
            var tenantId = string.IsNullOrWhiteSpace(registration.TenantId) ? "common" : registration.TenantId;
            var authenticationService = new OneDriveAuthenticationService(registration.ClientId, tenantId, cacheKeyPrefix: registration.RegistrationId);
            var apiClient = new OneDriveApiClient(authenticationService);
            var rootVolume = new OneDriveVolumeSource(apiClient, registration.RegistrationId, BuildDisplayName(registration), _allowWriteAccess);

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

    public IReadOnlyList<CloudRootDefinition> GetAvailableRoots() =>
        _registrations
            .Select(registration => new CloudRootDefinition(
                OneDrivePath.BuildRootPath(registration.RegistrationId),
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

    private OneDriveVolumeSource CreateRootVolume(CloudProviderAppRegistration registration)
    {
        var tenantId = string.IsNullOrWhiteSpace(registration.TenantId) ? "common" : registration.TenantId;
        var authenticationService = new OneDriveAuthenticationService(registration.ClientId, tenantId, cacheKeyPrefix: registration.RegistrationId);
        var apiClient = new OneDriveApiClient(authenticationService);
        return new OneDriveVolumeSource(apiClient, registration.RegistrationId, BuildDisplayName(registration), _allowWriteAccess);
    }

    private static string BuildDisplayName(CloudProviderAppRegistration registration) =>
        string.IsNullOrWhiteSpace(registration.Alias)
            ? "OneDrive"
            : $"OneDrive - {registration.Alias.Trim()}";
}