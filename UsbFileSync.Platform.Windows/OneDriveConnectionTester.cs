namespace UsbFileSync.Platform.Windows;

public static class OneDriveConnectionTester
{
    public static async Task TestConnectionAsync(string clientId, string? tenantId = null, string? cacheKeyPrefix = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var authenticationService = new OneDriveAuthenticationService(clientId, string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId, cacheKeyPrefix: cacheKeyPrefix);
        var apiClient = new OneDriveApiClient(authenticationService);

        var accessToken = await authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("OneDrive sign-in did not return an access token.");
        }

        _ = await apiClient.EnumerateAsync(string.Empty, cancellationToken).ConfigureAwait(false);
    }
}