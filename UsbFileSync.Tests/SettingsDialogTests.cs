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
    public void TryCreateSerializableCloudAccounts_NormalizesAndSerializesAccounts()
    {
        var cloudRoot = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cloudRoot);
        try
        {
            var cloudAccounts = new[]
            {
                new CloudAccountRegistrationViewModel
                {
                    Id = "account-1",
                    Provider = CloudStorageProvider.Dropbox,
                    Login = " personal@example.com ",
                    LocalRootPath = cloudRoot,
                },
            };

            var success = SettingsDialog.TryCreateSerializableCloudAccounts(cloudAccounts, out var serializedAccounts, out var errorMessage);

            Assert.True(success);
            Assert.Equal(string.Empty, errorMessage);
            var account = Assert.Single(serializedAccounts);
            Assert.Equal("account-1", account.Id);
            Assert.Equal(CloudStorageProvider.Dropbox, account.Provider);
            Assert.Equal("personal@example.com", account.Login);
            Assert.Equal(Path.GetFullPath(cloudRoot), account.LocalRootPath);
        }
        finally
        {
            Directory.Delete(cloudRoot, recursive: true);
        }
    }

    [Fact]
    public void TryCreateSerializableCloudAccounts_RejectsDuplicateProviderAndLogin()
    {
        var cloudRootA = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));
        var cloudRootB = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cloudRootA);
        Directory.CreateDirectory(cloudRootB);
        try
        {
            var cloudAccounts = new[]
            {
                new CloudAccountRegistrationViewModel
                {
                    Provider = CloudStorageProvider.GoogleDrive,
                    Login = "same@example.com",
                    LocalRootPath = cloudRootA,
                },
                new CloudAccountRegistrationViewModel
                {
                    Provider = CloudStorageProvider.GoogleDrive,
                    Login = "same@example.com",
                    LocalRootPath = cloudRootB,
                },
            };

            var success = SettingsDialog.TryCreateSerializableCloudAccounts(cloudAccounts, out _, out var errorMessage);

            Assert.False(success);
            Assert.Contains("already listed", errorMessage);
        }
        finally
        {
            Directory.Delete(cloudRootA, recursive: true);
            Directory.Delete(cloudRootB, recursive: true);
        }
    }
}
