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
                ClientId = " google-client-id "
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
                Assert.Equal(string.Empty, google.TenantId);
            },
            oneDrive =>
            {
                Assert.Equal(CloudStorageProvider.OneDrive, oneDrive.Provider);
                Assert.Equal("onedrive-client-id", oneDrive.ClientId);
                Assert.Equal("contoso-tenant", oneDrive.TenantId);
            });
    }

    [Fact]
    public void TryCreateCloudProviderAppRegistrations_DefaultsOneDriveTenantToCommon()
    {
        var registrations = new[]
        {
            new CloudProviderAppRegistrationViewModel(CloudStorageProvider.OneDrive)
            {
                ClientId = "onedrive-client-id",
                TenantId = " "
            },
        };

        var success = SettingsDialog.TryCreateCloudProviderAppRegistrations(registrations, out var serializedRegistrations, out _);

        Assert.True(success);
        var registration = Assert.Single(serializedRegistrations);
        Assert.Equal(CloudStorageProvider.OneDrive, registration.Provider);
        Assert.Equal("common", registration.TenantId);
    }
}
