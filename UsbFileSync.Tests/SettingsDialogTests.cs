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
    public void TryCreateExcludedPathPatterns_NormalizesAndDeduplicatesPatterns()
    {
        const string text = " node_modules \r\n.venv\nobj\nnode_modules\ncache/*/tmp ";

        var success = SettingsDialog.TryCreateExcludedPathPatterns(text, out var patterns, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal(["node_modules", ".venv", "obj", "cache/*/tmp"], patterns);
    }

    [Fact]
    public void TryCreateExcludedPathPatterns_RejectsAbsolutePaths()
    {
        var success = SettingsDialog.TryCreateExcludedPathPatterns("C:/temp", out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("absolute path", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateImageRenameFileNamePatterns_IncludesDefaultsAndCustomMasks()
    {
        var defaults = new[]
        {
            new SelectableTextOptionViewModel { Label = "IMG_????", Value = "IMG_????", IsSelected = true },
            new SelectableTextOptionViewModel { Label = "DSC_????", Value = "DSC_????", IsSelected = false },
        };
        var customEntries = new[]
        {
            new EditableSelectableTextOptionViewModel { Value = "gopr????", IsSelected = true },
            new EditableSelectableTextOptionViewModel { Value = "IMG_????", IsSelected = true },
            new EditableSelectableTextOptionViewModel { Value = "MVI_????", IsSelected = false },
        };

        var success = SettingsDialog.TryCreateImageRenameFileNamePatterns(defaults, customEntries, out var patterns, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal(["IMG_????", "GOPR????"], patterns);
    }

    [Fact]
    public void TryCreateImageRenameExtensions_NormalizesAndDeduplicatesExtensions()
    {
        var defaults = new[]
        {
            new SelectableTextOptionViewModel { Label = ".jpg", Value = ".jpg", IsSelected = true },
            new SelectableTextOptionViewModel { Label = ".jpeg", Value = ".jpeg", IsSelected = false },
        };
        var customEntries = new[]
        {
            new EditableSelectableTextOptionViewModel { Value = "jpeg", IsSelected = true },
            new EditableSelectableTextOptionViewModel { Value = ".jpg", IsSelected = true },
            new EditableSelectableTextOptionViewModel { Value = ".heic", IsSelected = false },
        };

        var success = SettingsDialog.TryCreateImageRenameExtensions(defaults, customEntries, out var extensions, out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal([".jpg", ".jpeg"], extensions);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_StoresConfiguredProvidersOnly()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                RegistrationId = "google-account",
                Alias = "Personal Drive",
                ClientId = " google-client-id ",
                ClientSecret = " google-secret "
            },
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.Dropbox),
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                RegistrationId = "onedrive-account",
                Alias = "Personal OneDrive",
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
                Assert.Equal("google-account", google.RegistrationId);
                Assert.Equal(CloudStorageProvider.GoogleDrive, google.Provider);
                Assert.Equal("Personal Drive", google.Alias);
                Assert.Equal("google-client-id", google.ClientId);
                Assert.Equal("google-secret", google.ClientSecret);
                Assert.Equal(string.Empty, google.TenantId);
            },
            oneDrive =>
            {
                Assert.Equal("onedrive-account", oneDrive.RegistrationId);
                Assert.Equal(CloudStorageProvider.OneDrive, oneDrive.Provider);
                Assert.Equal("Personal OneDrive", oneDrive.Alias);
                Assert.Equal("onedrive-client-id", oneDrive.ClientId);
                Assert.Equal("consumers", oneDrive.TenantId);
            });
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_ForcesOneDriveTenantToConsumers()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                Alias = "Consumer Account",
                ClientId = "onedrive-client-id",
                TenantId = "contoso-tenant"
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out var serializedRegistrations, out _);

        Assert.True(success);
        var registration = Assert.Single(serializedRegistrations);
        Assert.Equal(CloudStorageProvider.OneDrive, registration.Provider);
        Assert.Equal("consumers", registration.TenantId);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_RequiresAliasForConfiguredAccounts()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.Dropbox)
            {
                ClientId = "dropbox-app-key"
            }
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("Enter an alias", errorMessage);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_RejectsDuplicateAliasesPerProvider()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                Alias = "Work",
                ClientId = "google-a"
            },
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                Alias = "Work",
                ClientId = "google-b"
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out _, out var errorMessage);

        Assert.False(success);
        Assert.Contains("listed more than once", errorMessage);
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_ExtractsGoogleCredentialsFromDesktopClientJson()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
            {
                                Alias = "Imported Google Drive",
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
        Assert.True(dropbox.UsesClientSecret);
    }

    [Fact]
    public void CanTestCloudProviderConnection_RequiresCustomModeAndClientId()
    {
        var registration = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive)
        {
            ClientId = "google-client-id"
        };

        Assert.False(SettingsDialog.CanTestCloudProviderConnection(false, registration));
        Assert.True(SettingsDialog.CanTestCloudProviderConnection(true, registration));
    }

    [Fact]
    public void GetCloudProviderConnectionGuidance_ExplainsWhyTestingIsUnavailable()
    {
        var googleDrive = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.GoogleDrive);
        var dropbox = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.Dropbox);
        var oneDrive = new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive);

        Assert.Contains("Turn on", SettingsDialog.GetCloudProviderConnectionGuidance(false, googleDrive));
        Assert.Contains("Enter a Google OAuth client ID", SettingsDialog.GetCloudProviderConnectionGuidance(true, googleDrive));

        googleDrive.ClientId = "configured-client-id";

        Assert.Equal("Google Drive is ready to test. Add a client secret too if your Google OAuth client requires one.", SettingsDialog.GetCloudProviderConnectionGuidance(true, googleDrive));

        googleDrive.ClientSecret = "configured-secret";

        Assert.Equal("Google Drive is ready to test.", SettingsDialog.GetCloudProviderConnectionGuidance(true, googleDrive));

        Assert.Contains("Enter a Dropbox app key", SettingsDialog.GetCloudProviderConnectionGuidance(true, dropbox));

        dropbox.ClientId = "dropbox-app-key";

        Assert.Equal("Dropbox is ready to test. In your Dropbox app settings, register redirect URI 'http://127.0.0.1:53682/'. Add an app secret too if your Dropbox app requires it.", SettingsDialog.GetCloudProviderConnectionGuidance(true, dropbox));

        dropbox.ClientSecret = "dropbox-app-secret";

        Assert.Equal("Dropbox is ready to test. In your Dropbox app settings, register redirect URI 'http://127.0.0.1:53682/'.", SettingsDialog.GetCloudProviderConnectionGuidance(true, dropbox));

        Assert.Contains("Enter a Microsoft application client ID", SettingsDialog.GetCloudProviderConnectionGuidance(true, oneDrive));

        oneDrive.ClientId = "configured-client-id";

        Assert.Equal("OneDrive is ready to test. UsbFileSync uses the fixed 'consumers' tenant.", SettingsDialog.GetCloudProviderConnectionGuidance(true, oneDrive));
    }

    [Fact]
    public void GetCloudProviderTestFailureMessage_AddsDropboxPermissionGuidance_ForFolderListingFailures()
    {
        var message = typeof(SettingsDialog)
            .GetMethod("GetCloudProviderTestFailureMessage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [CloudStorageProvider.Dropbox, "Dropbox folder listing failed (409 Conflict)."]);

        Assert.NotNull(message);
        Assert.Contains("files.metadata.read", message!.ToString());
        Assert.Contains("files.content.write", message.ToString());
    }
}
