using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class DropboxTokenStoreTests
{
    [Fact]
    public void Load_ReturnsSavedToken_ForRegistrationScopedCacheKey()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSyncTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DropboxTokenStore(rootDirectory);
            var token = new DropboxAuthToken(
                AccessToken: "cached-token",
                RefreshToken: "refresh-token",
                ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
                Scope: string.Empty);

            store.Save("dropbox-account|dropbox-rw", token);

            var reloadedToken = store.Load("dropbox-account|dropbox-rw");

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