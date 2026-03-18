using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Tests;

public sealed class JsonSyncSettingsStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsNull_WhenSettingsFileDoesNotExist()
    {
        var store = new JsonSyncSettingsStore(Path.Combine(_rootPath, "settings.json"));

        var configuration = store.Load();

        Assert.Null(configuration);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsConfiguration()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            Mode = SyncMode.TwoWay,
            DetectMoves = false,
            DryRun = true,
            VerifyChecksums = true,
            MoveMode = true,
            IncludeSubfolders = false,
            HideMacOsSystemFiles = false,
            ExcludedPathPatterns = ["node_modules", ".venv", "bin", "obj"],
            ParallelCopyCount = 4,
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(configuration.SourcePath, restored.SourcePath);
        Assert.Equal(configuration.DestinationPath, restored.DestinationPath);
        Assert.Equal(configuration.Mode, restored.Mode);
        Assert.Equal(configuration.DetectMoves, restored.DetectMoves);
        Assert.Equal(configuration.DryRun, restored.DryRun);
        Assert.Equal(configuration.VerifyChecksums, restored.VerifyChecksums);
        Assert.Equal(configuration.MoveMode, restored.MoveMode);
        Assert.Equal(configuration.IncludeSubfolders, restored.IncludeSubfolders);
        Assert.Equal(configuration.HideMacOsSystemFiles, restored.HideMacOsSystemFiles);
        Assert.Equal(configuration.ExcludedPathPatterns, restored.ExcludedPathPatterns);
        Assert.Equal(configuration.ParallelCopyCount, restored.ParallelCopyCount);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsUnlimitedParallelCopyCount()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            ParallelCopyCount = 0,
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal(0, restored.ParallelCopyCount);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsMultipleDestinationPaths()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            DestinationPaths = [@"F:\BackupDrive", @"G:\ArchiveDrive"],
            Mode = SyncMode.OneWay,
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Equal([@"F:\BackupDrive", @"G:\ArchiveDrive"], restored.GetDestinationPaths());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsCloudProviderAppRegistrations()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            CloudProviderAppRegistrations =
            [
                new CloudProviderAppRegistration
                {
                    RegistrationId = "google-account",
                    Provider = CloudStorageProvider.GoogleDrive,
                    Alias = "Personal Drive",
                    ClientId = "google-client-id",
                    ClientSecret = "google-secret"
                },
                new CloudProviderAppRegistration
                {
                    RegistrationId = "onedrive-account",
                    Provider = CloudStorageProvider.OneDrive,
                    Alias = "Personal OneDrive",
                    ClientId = "onedrive-client-id",
                    TenantId = "common"
                }
            ],
            UseCustomCloudProviderCredentials = true,
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.Collection(
            restored.CloudProviderAppRegistrations,
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
                Assert.Equal("common", oneDrive.TenantId);
            });
        Assert.True(restored.UseCustomCloudProviderCredentials);
    }

    [Fact]
    public void Save_ThenLoad_PreservesCustomRegistrationsWhenBuiltInModeIsPreferred()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        var store = new JsonSyncSettingsStore(settingsPath);
        var configuration = new SyncConfiguration
        {
            SourcePath = @"E:\MainDrive",
            DestinationPath = @"F:\BackupDrive",
            UseCustomCloudProviderCredentials = false,
            CloudProviderAppRegistrations =
            [
                new CloudProviderAppRegistration
                {
                    RegistrationId = "dropbox-account",
                    Provider = CloudStorageProvider.Dropbox,
                    Alias = "Team Dropbox",
                    ClientId = "dropbox-client-id"
                }
            ]
        };

        store.Save(configuration);
        var restored = store.Load();

        Assert.NotNull(restored);
        Assert.False(restored.UseCustomCloudProviderCredentials);
        var registration = Assert.Single(restored.CloudProviderAppRegistrations);
        Assert.Equal("dropbox-account", registration.RegistrationId);
        Assert.Equal(CloudStorageProvider.Dropbox, registration.Provider);
        Assert.Equal("Team Dropbox", registration.Alias);
        Assert.Equal("dropbox-client-id", registration.ClientId);
    }

    [Fact]
    public void Load_ReturnsNull_WhenSettingsFileContainsInvalidJson()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json");
        var store = new JsonSyncSettingsStore(settingsPath);

        var configuration = store.Load();

        Assert.Null(configuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
