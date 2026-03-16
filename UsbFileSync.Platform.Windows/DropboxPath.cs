namespace UsbFileSync.Platform.Windows;

public static class DropboxPath
{
    public const string RootPath = "dropbox://root";

    public static bool IsDropboxPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("dropbox://", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? path, out string? registrationId, out string relativePath) =>
        CloudAccountPath.TryParse("dropbox", path, out registrationId, out relativePath);

    public static bool TryParse(string? path, out string relativePath) =>
        TryParse(path, out _, out relativePath);

    public static string BuildRootPath(string? registrationId) => CloudAccountPath.BuildRootPath("dropbox", registrationId);

    public static string BuildPath(string? relativePath) => BuildPath(null, relativePath);

    public static string BuildPath(string? registrationId, string? relativePath) =>
        CloudAccountPath.BuildPath("dropbox", registrationId, relativePath);
}