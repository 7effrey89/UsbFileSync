namespace UsbFileSync.Platform.Windows;

public static class DropboxConnectionTester
{
    public static async Task TestConnectionAsync(string clientId, string? clientSecret = null, string? tokenCacheKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var authenticationService = new DropboxAuthenticationService(clientId, clientSecret, tokenCacheKey: tokenCacheKey);
        var apiClient = new DropboxApiClient(authenticationService);

        _ = await apiClient.EnumerateAsync(string.Empty, cancellationToken).ConfigureAwait(false);
    }
}