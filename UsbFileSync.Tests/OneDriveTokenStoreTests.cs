using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class OneDriveTokenStoreTests
{
    [Fact]
    public void Load_ReturnsSavedToken_ForConsumersCacheKey()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSyncTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OneDriveTokenStore(rootDirectory);
            var token = new OneDriveAuthToken(
                AccessToken: "cached-token",
                RefreshToken: "refresh-token",
                ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
                Scope: "Files.ReadWrite offline_access User.Read");

            store.Save("client-id|consumers|graph-files-rw", token);

            var reloadedToken = store.Load("client-id|consumers|graph-files-rw");

            Assert.NotNull(reloadedToken);
            Assert.Equal("cached-token", reloadedToken!.AccessToken);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }
}