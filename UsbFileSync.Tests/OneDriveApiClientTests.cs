using System.Net;
using System.Text;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.Tests;

public sealed class OneDriveApiClientTests
{
    [Fact]
    public async Task CreateDirectoryAsync_ClearsChildrenCache_SoNextEnumerationIncludesCreatedFolder()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSyncTests", Guid.NewGuid().ToString("N"));
        try
        {
            var tokenStore = new OneDriveTokenStore(rootDirectory);
            tokenStore.Save(
                "client-id|common|graph-files-rw",
                new OneDriveAuthToken(
                    AccessToken: "cached-token",
                    RefreshToken: "refresh-token",
                    ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
                    Scope: "Files.ReadWrite offline_access User.Read"));

            var authenticationService = new OneDriveAuthenticationService("client-id", "common", tokenStore: tokenStore);
            var handler = new FakeOneDriveHandler();
            using var httpClient = new HttpClient(handler);
            var apiClient = new OneDriveApiClient(authenticationService, httpClient);

            var initialItems = await apiClient.EnumerateAsync(string.Empty);
            Assert.Single(initialItems);
            Assert.Equal("Existing", initialItems[0].Name);

            await apiClient.CreateDirectoryAsync("USBTest");

            var refreshedItems = await apiClient.EnumerateAsync(string.Empty);
            Assert.Equal(2, refreshedItems.Count);
            Assert.Contains(refreshedItems, item => item.Name == "USBTest" && item.IsDirectory);
            Assert.Equal(2, handler.RootChildrenRequests);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    private sealed class FakeOneDriveHandler : HttpMessageHandler
    {
        public int RootChildrenRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && path.EndsWith("/items/root/children", StringComparison.OrdinalIgnoreCase))
            {
                RootChildrenRequests++;
                var payload = RootChildrenRequests == 1
                    ? """
                    {"value":[{"id":"existing-id","name":"Existing","folder":{},"size":0,"lastModifiedDateTime":"2026-03-16T12:00:00Z"}]}
                    """
                    : """
                    {"value":[{"id":"existing-id","name":"Existing","folder":{},"size":0,"lastModifiedDateTime":"2026-03-16T12:00:00Z"},{"id":"new-folder-id","name":"USBTest","folder":{},"size":0,"lastModifiedDateTime":"2026-03-16T12:05:00Z"}]}
                    """;
                return Task.FromResult(CreateJsonResponse(payload));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/root:/USBTest", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/items/root/children", StringComparison.OrdinalIgnoreCase))
            {
                const string payload = """
                    {"id":"new-folder-id","name":"USBTest","folder":{},"size":0,"lastModifiedDateTime":"2026-03-16T12:05:00Z"}
                    """;
                return Task.FromResult(CreateJsonResponse(payload));
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage CreateJsonResponse(string payload) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
    }
}