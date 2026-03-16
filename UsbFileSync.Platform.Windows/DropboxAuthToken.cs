namespace UsbFileSync.Platform.Windows;

internal sealed record DropboxAuthToken(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    string Scope);