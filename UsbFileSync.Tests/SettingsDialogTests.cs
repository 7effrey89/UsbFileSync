using UsbFileSync.App;
using UsbFileSync.App.Services;
using UsbFileSync.App.ViewModels;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Tests;

public sealed class SettingsDialogTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData("32", 32)]
    public void TryParseParallelCopyCount_AcceptsZeroAndPositiveValues(string text, int expected)
    {
        var success = SettingsDialog.TryParseParallelCopyCount(text, out var value);

        Assert.True(success);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void TryParseParallelCopyCount_RejectsInvalidValues(string text)
    {
        var success = SettingsDialog.TryParseParallelCopyCount(text, out _);

        Assert.False(success);
    }

    [Fact]
    public void TryCreateSerializableMappings_NormalizesExtensionsAndSerializesProviders()
    {
        var mappings = new[]
        {
            new PreviewProviderMappingViewModel { Extension = "txt", ProviderKind = PreviewProviderKind.Text },
            new PreviewProviderMappingViewModel { Extension = ".pdf", ProviderKind = PreviewProviderKind.Pdf },
        };

        var success = SettingsDialog.TryCreateSerializableMappings(mappings, out var serializedMappings, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal("Text", serializedMappings[".txt"]);
        Assert.Equal("Pdf", serializedMappings[".pdf"]);
    }

    [Fact]
    public void TryCreateSerializableMappings_SerializesOfficeProvider()
    {
        var mappings = new[]
        {
            new PreviewProviderMappingViewModel { Extension = ".docx", ProviderKind = PreviewProviderKind.Office },
        };

        var success = SettingsDialog.TryCreateSerializableMappings(mappings, out var serializedMappings, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal("Office", serializedMappings[".docx"]);
    }

    [Fact]
    public void TryCreateSerializableMappings_RejectsDuplicateExtensions()
    {
        var mappings = new[]
        {
            new PreviewProviderMappingViewModel { Extension = ".txt", ProviderKind = PreviewProviderKind.Text },
            new PreviewProviderMappingViewModel { Extension = "txt", ProviderKind = PreviewProviderKind.Unsupported },
        };

        var success = SettingsDialog.TryCreateSerializableMappings(mappings, out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("listed more than once", errorMessage);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_StoresConfiguredProvidersOnly()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                ClientId = " google-client-id ",
                ClientSecret = " google-secret "
            },
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.Dropbox),
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                ClientId = "onedrive-client-id",
                TenantId = "contoso-tenant"
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out var serializedRegistrations, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Collection(
            serializedRegistrations,
            google =>
            {
                Assert.Equal(CloudStorageProvider.GoogleDrive, google.Provider);
                Assert.Equal("google-client-id", google.ClientId);
                Assert.Equal("google-secret", google.ClientSecret);
                Assert.Equal(string.Empty, google.TenantId);
            },
            oneDrive =>
            {
                Assert.Equal(CloudStorageProvider.OneDrive, oneDrive.Provider);
                Assert.Equal("onedrive-client-id", oneDrive.ClientId);
                Assert.Equal("common", oneDrive.TenantId);
            });
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_ForcesOneDriveTenantToCommon()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                ClientId = "onedrive-client-id",
                TenantId = "contoso-tenant"
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out var serializedRegistrations, out _);

        Assert.True(success);
        var registration = Assert.Single(serializedRegistrations);
        Assert.Equal(CloudStorageProvider.OneDrive, registration.Provider);
        Assert.Equal("common", registration.TenantId);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_ExtractsGoogleCredentialsFromDesktopClientJson()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                ClientSecret = """
                    {
                      "installed": {
                        "client_id": "desktop-client-id.apps.googleusercontent.com",
                        "client_secret": "desktop-client-secret"
                      }
                    }
                    """
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out var serializedRegistrations, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        var registration = Assert.Single(serializedRegistrations);
        Assert.Equal("desktop-client-id.apps.googleusercontent.com", registration.ClientId);
        Assert.Equal("desktop-client-secret", registration.ClientSecret);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_RejectsMalformedGoogleJson()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                ClientSecret = "{ not valid json"
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("could not be parsed", errorMessage);
    }

    [Fact]
    public void CloudProviderAppRegistrationViewModel_ExposesBuiltInModeMetadata()
    {
        var oneDrive = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive);
        var dropbox = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.Dropbox);

        Assert.Equal("OneDrive", oneDrive.ProviderDisplayName);
        Assert.True(oneDrive.UsesTenantId);
        Assert.Equal("Dropbox", dropbox.ProviderDisplayName);
        Assert.False(dropbox.UsesTenantId);
    }

    [Fact]
    public void CanTestGoogleDriveConnection_RequiresCustomModeAndClientId()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                ClientId = "google-client-id"
            },
        };

        Assert.False(SettingsDialog.CanTestGoogleDriveConnection(false, registrations));
        Assert.True(SettingsDialog.CanTestGoogleDriveConnection(true, registrations));
    }

    [Fact]
    public void CanTestOneDriveConnection_RequiresCustomModeAndClientId()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                ClientId = "onedrive-client-id"
            },
        };

        Assert.False(SettingsDialog.CanTestOneDriveConnection(false, registrations));
        Assert.True(SettingsDialog.CanTestOneDriveConnection(true, registrations));
    }

    [Fact]
    public void GetGoogleDriveConnectionGuidance_ExplainsWhyTestingIsUnavailable()
    {
        var googleDrive = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive);

        Assert.Contains("Turn on", SettingsDialog.GetGoogleDriveConnectionGuidance(false, googleDrive));
        Assert.Contains("Enter a Google OAuth client ID", SettingsDialog.GetGoogleDriveConnectionGuidance(true, googleDrive));

        googleDrive.ClientId = "configured-client-id";

        Assert.Equal("Google Drive is ready to test. Add a client secret too if your Google OAuth client requires one.", SettingsDialog.GetGoogleDriveConnectionGuidance(true, googleDrive));

        googleDrive.ClientSecret = "configured-secret";

        Assert.Equal("Google Drive is ready to test.", SettingsDialog.GetGoogleDriveConnectionGuidance(true, googleDrive));
    }

    [Fact]
    public void GetOneDriveConnectionGuidance_ExplainsWhyTestingIsUnavailable()
    {
        var oneDrive = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive);

        Assert.Contains("Turn on", SettingsDialog.GetOneDriveConnectionGuidance(false, oneDrive));
        Assert.Contains("Enter a Microsoft application client ID", SettingsDialog.GetOneDriveConnectionGuidance(true, oneDrive));

        oneDrive.ClientId = "configured-client-id";

        Assert.Equal("OneDrive is ready to test. UsbFileSync uses the fixed 'common' tenant.", SettingsDialog.GetOneDriveConnectionGuidance(true, oneDrive));
    }
}
