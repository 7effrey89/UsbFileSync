namespace UsbFileSync.Platform.Windows;

internal sealed record OneDriveAuthToken(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    string Scope);