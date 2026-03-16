using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class DropboxAuthenticationServiceTests
{
    [Fact]
    public void RedirectUri_UsesFixedLoopbackCallback()
    {
        Assert.Equal("http://127.0.0.1:53682/", DropboxAuthenticationService.RedirectUri);
    }

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData("", "", "", true)]
    [InlineData("state-value", null, null, false)]
    [InlineData(null, "auth-code", null, false)]
    [InlineData(null, null, "access_denied", false)]
    public void ShouldIgnoreCallbackRequest_OnlyIgnoresRequestsWithoutOAuthParameters(string? state, string? code, string? error, bool expected)
    {
        var shouldIgnore = DropboxAuthenticationService.ShouldIgnoreCallbackRequest(state, code, error);

        Assert.Equal(expected, shouldIgnore);
    }

    [Fact]
    public void InvalidateCachedToken_DeletesCachedToken()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSyncTests", Guid.NewGuid().ToString("N"));

        try
        {
            var tokenStore = new DropboxTokenStore(rootDirectory);
            tokenStore.Save(
                "dropbox-registration|dropbox-rw",
                new DropboxAuthToken(
                    AccessToken: "stale-token",
                    RefreshToken: string.Empty,
                    ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
                    Scope: "files.content.read"));

            var authenticationService = new DropboxAuthenticationService(
                "client-id",
                tokenCacheKey: "dropbox-registration",
                tokenStore: tokenStore,
                openBrowser: _ => false);

            authenticationService.InvalidateCachedToken();

            Assert.Null(tokenStore.Load("dropbox-registration|dropbox-rw"));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReusesCachedToken_EvenWhenScopeMetadataIsIncomplete()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSyncTests", Guid.NewGuid().ToString("N"));

        try
        {
            var tokenStore = new DropboxTokenStore(rootDirectory);
            tokenStore.Save(
                "dropbox-registration|dropbox-rw",
                new DropboxAuthToken(
                    AccessToken: "cached-token",
                    RefreshToken: string.Empty,
                    ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
                    Scope: string.Empty));

            var browserOpened = false;
            var authenticationService = new DropboxAuthenticationService(
                "client-id",
                tokenCacheKey: "dropbox-registration",
                tokenStore: tokenStore,
                openBrowser: _ =>
                {
                    browserOpened = true;
                    return true;
                });

            var accessToken = await authenticationService.GetAccessTokenAsync();

            Assert.Equal("cached-token", accessToken);
            Assert.False(browserOpened);
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