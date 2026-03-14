using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Tests;

public sealed class JsonCloudAccountStoreTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "UsbFileSync.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsEmptyList_WhenSettingsFileDoesNotExist()
    {
        var store = new JsonCloudAccountStore(Path.Combine(_rootPath, "cloud-accounts.json"));

        var accounts = store.Load();

        Assert.Empty(accounts);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsCloudAccounts()
    {
        Directory.CreateDirectory(_rootPath);
        var localRootA = Path.Combine(_rootPath, "googledrive");
        var localRootB = Path.Combine(_rootPath, "onedrive");
        Directory.CreateDirectory(localRootA);
        Directory.CreateDirectory(localRootB);
        var store = new JsonCloudAccountStore(Path.Combine(_rootPath, "cloud-accounts.json"));

        store.Save(
        [
            new CloudAccountRegistration
            {
                Id = "gdrive-main",
                Provider = CloudStorageProvider.GoogleDrive,
                Login = "main@example.com",
                LocalRootPath = localRootA,
            },
            new CloudAccountRegistration
            {
                Id = "onedrive-work",
                Provider = CloudStorageProvider.OneDrive,
                Login = "work@example.com",
                LocalRootPath = localRootB,
            },
        ]);

        var restored = store.Load();

        Assert.Collection(
            restored,
            account =>
            {
                Assert.Equal("gdrive-main", account.Id);
                Assert.Equal(CloudStorageProvider.GoogleDrive, account.Provider);
                Assert.Equal("main@example.com", account.Login);
                Assert.Equal(localRootA, account.LocalRootPath);
            },
            account =>
            {
                Assert.Equal("onedrive-work", account.Id);
                Assert.Equal(CloudStorageProvider.OneDrive, account.Provider);
                Assert.Equal("work@example.com", account.Login);
                Assert.Equal(localRootB, account.LocalRootPath);
            });
    }

    [Fact]
    public void Load_ReturnsEmptyList_WhenSettingsFileContainsInvalidJson()
    {
        Directory.CreateDirectory(_rootPath);
        var settingsPath = Path.Combine(_rootPath, "cloud-accounts.json");
        File.WriteAllText(settingsPath, "{ invalid json");
        var store = new JsonCloudAccountStore(settingsPath);

        var accounts = store.Load();

        Assert.Empty(accounts);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
