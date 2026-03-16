using System.Net;
using System.Net.Http;
using System.Text;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class DropboxApiClientTests
{
    [Fact]
    public async Task EnumerateAsync_InvalidatesCachedTokenAndStops_WhenDropboxReportsMissingScope()
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
                    Scope: string.Empty));

            var authenticationService = new DropboxAuthenticationService(
                "client-id",
                tokenCacheKey: "dropbox-registration",
                tokenStore: tokenStore,
                openBrowser: _ => throw new InvalidOperationException("OAuth browser should not be launched during missing-scope invalidation."));
            var httpClient = new HttpClient(new MissingScopeMessageHandler());
            var apiClient = new DropboxApiClient(authenticationService, httpClient);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => apiClient.EnumerateAsync(string.Empty));

            Assert.Contains("missing_scope", exception.Message);
            Assert.Contains("cleared the cached Dropbox sign-in", exception.Message);
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

    private sealed class MissingScopeMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent(
                    "{\"error_summary\":\"missing_scope/..\",\"error\":{\".tag\":\"missing_scope\"}}",
                    Encoding.UTF8,
                    "application/json")
            };

            return Task.FromResult(response);
        }
    }
}