namespace UsbFileSync.Platform.Windows;

public static class GoogleDrivePath
{
    public const string RootPath = "gdrive://root";

    public static bool IsGoogleDrivePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("gdrive://", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? path, out string? registrationId, out string relativePath) =>
        CloudAccountPath.TryParse("gdrive", path, out registrationId, out relativePath);

    public static bool TryParse(string? path, out string relativePath)
    {
        return TryParse(path, out _, out relativePath);
    }

    public static string BuildRootPath(string? registrationId) => CloudAccountPath.BuildRootPath("gdrive", registrationId);

    public static string BuildPath(string? relativePath)
    {
        return BuildPath(null, relativePath);
    }

    public static string BuildPath(string? registrationId, string? relativePath) =>
        CloudAccountPath.BuildPath("gdrive", registrationId, relativePath);
}