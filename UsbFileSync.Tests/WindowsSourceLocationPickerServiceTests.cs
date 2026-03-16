using UsbFileSync.App.Services;
using UsbFileSync.Core.Services;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class WindowsSourceLocationPickerServiceTests
{
    [Fact]
    public void GetDestinationBrowseVolumeService_UsesReadOnlyExtProbe_ForExtDestinationService()
    {
        var originalService = new ExtVolumeService(allowWriteAccess: true);
        var replacementService = new StubSourceVolumeService();

        var browseService = WindowsSourceLocationPickerService.GetDestinationBrowseVolumeService(
            originalService,
            () => replacementService);

        Assert.Same(replacementService, browseService);
        Assert.NotSame(originalService, browseService);
    }

    [Fact]
    public void GetDestinationBrowseVolumeService_KeepsCustomDestinationService_WhenNotExtVolumeService()
    {
        var originalService = new StubSourceVolumeService();
        var replacementService = new StubSourceVolumeService();

        var browseService = WindowsSourceLocationPickerService.GetDestinationBrowseVolumeService(
            originalService,
            () => replacementService);

        Assert.Same(originalService, browseService);
    }

    [Fact]
    public void CreateDestinationBrowseVolumeService_ResolvesGoogleDriveRoot_WhenCustomCredentialsAreConfigured()
    {
        var registrations = new[]
        {
            new UsbFileSync.Core.Models.CloudProviderAppRegistration
            {
                Provider = UsbFileSync.Core.Models.CloudStorageProvider.GoogleDrive,
                ClientId = "google-client-id",
                ClientSecret = "google-client-secret"
            }
        };

        var browseService = SyncVolumeServiceFactory.CreateDestinationBrowseVolumeService(true, registrations);

        var success = browseService.TryCreateVolume(GoogleDrivePath.RootPath, out var volume, out var failureReason);

        Assert.True(success);
        Assert.NotNull(volume);
        Assert.False(volume!.IsReadOnly);
        Assert.Equal("Google Drive", volume.FileSystemType);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void CreateDestinationVolumeService_ResolvesWritableGoogleDriveRoot_WhenCustomCredentialsAreConfigured()
    {
        var registrations = new[]
        {
            new UsbFileSync.Core.Models.CloudProviderAppRegistration
            {
                Provider = UsbFileSync.Core.Models.CloudStorageProvider.GoogleDrive,
                ClientId = "google-client-id",
                ClientSecret = "google-client-secret"
            }
        };

        var destinationService = SyncVolumeServiceFactory.CreateDestinationVolumeService(true, registrations);

        var success = destinationService.TryCreateVolume(GoogleDrivePath.RootPath, out var volume, out var failureReason);

        Assert.True(success);
        Assert.NotNull(volume);
        Assert.False(volume!.IsReadOnly);
        Assert.Equal("Google Drive", volume.FileSystemType);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void CreateDestinationBrowseVolumeService_ResolvesOneDriveRoot_WhenCustomCredentialsAreConfigured()
    {
        var registrations = new[]
        {
            new UsbFileSync.Core.Models.CloudProviderAppRegistration
            {
                Provider = UsbFileSync.Core.Models.CloudStorageProvider.OneDrive,
                ClientId = "onedrive-client-id",
                TenantId = "common"
            }
        };

        var browseService = SyncVolumeServiceFactory.CreateDestinationBrowseVolumeService(true, registrations);

        var success = browseService.TryCreateVolume(OneDrivePath.RootPath, out var volume, out var failureReason);

        Assert.True(success);
        Assert.NotNull(volume);
        Assert.False(volume!.IsReadOnly);
        Assert.Equal("OneDrive", volume.FileSystemType);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void CreateDestinationVolumeService_ResolvesWritableOneDriveRoot_WhenCustomCredentialsAreConfigured()
    {
        var registrations = new[]
        {
            new UsbFileSync.Core.Models.CloudProviderAppRegistration
            {
                RegistrationId = "onedrive-account",
                Provider = UsbFileSync.Core.Models.CloudStorageProvider.OneDrive,
                Alias = "Personal OneDrive",
                ClientId = "onedrive-client-id",
                TenantId = "common"
            }
        };

        var destinationService = SyncVolumeServiceFactory.CreateDestinationVolumeService(true, registrations);

        var success = destinationService.TryCreateVolume(OneDrivePath.RootPath, out var volume, out var failureReason);

        Assert.True(success);
        Assert.NotNull(volume);
        Assert.False(volume!.IsReadOnly);
        Assert.Equal("OneDrive", volume.FileSystemType);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    [Fact]
    public void CreateDestinationBrowseVolumeService_ResolvesDropboxRoot_WhenCustomCredentialsAreConfigured()
    {
        var registrations = new[]
        {
            new UsbFileSync.Core.Models.CloudProviderAppRegistration
            {
                RegistrationId = "dropbox-account",
                Provider = UsbFileSync.Core.Models.CloudStorageProvider.Dropbox,
                Alias = "Team Dropbox",
                ClientId = "dropbox-app-key",
                ClientSecret = "dropbox-app-secret"
            }
        };

        var browseService = SyncVolumeServiceFactory.CreateDestinationBrowseVolumeService(true, registrations);

        var success = browseService.TryCreateVolume(DropboxPath.BuildRootPath("dropbox-account"), out var volume, out var failureReason);

        Assert.True(success);
        Assert.NotNull(volume);
        Assert.False(volume!.IsReadOnly);
        Assert.Equal("Dropbox", volume.FileSystemType);
        Assert.True(string.IsNullOrWhiteSpace(failureReason));
    }

    private sealed class StubSourceVolumeService : ISourceVolumeService
    {
        public bool TryCreateVolume(string path, out UsbFileSync.Core.Volumes.IVolumeSource? volume, out string? failureReason)
        {
            volume = null;
            failureReason = null;
            return false;
        }
    }
}