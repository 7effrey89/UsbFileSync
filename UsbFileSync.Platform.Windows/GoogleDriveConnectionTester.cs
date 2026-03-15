namespace UsbFileSync.Platform.Windows;

public static class GoogleDriveConnectionTester
{
    public static async Task TestConnectionAsync(string clientId, string? clientSecret = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var authenticationService = new GoogleDriveAuthenticationService(clientId, clientSecret);
        var apiClient = new GoogleDriveApiClient(authenticationService);

        // Force the real OAuth/token path so the settings-page test proves sign-in works.
        var accessToken = await authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Google Drive sign-in did not return an access token.");
        }

        _ = await apiClient.EnumerateAsync(string.Empty, cancellationToken).ConfigureAwait(false);
    }
}