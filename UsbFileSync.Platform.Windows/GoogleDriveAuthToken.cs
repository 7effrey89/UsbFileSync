namespace UsbFileSync.Platform.Windows;

internal sealed record GoogleDriveAuthToken(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    string Scope);