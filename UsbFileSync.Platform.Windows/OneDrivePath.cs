namespace UsbFileSync.Platform.Windows;

public static class OneDrivePath
{
    public const string RootPath = "onedrive://root";

    public static bool IsOneDrivePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith("onedrive://", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? path, out string? registrationId, out string relativePath) =>
        CloudAccountPath.TryParse("onedrive", path, out registrationId, out relativePath);

    public static bool TryParse(string? path, out string relativePath)
    {
        return TryParse(path, out _, out relativePath);
    }

    public static string BuildRootPath(string? registrationId) => CloudAccountPath.BuildRootPath("onedrive", registrationId);

    public static string BuildPath(string? relativePath)
    {
        return BuildPath(null, relativePath);
    }

    public static string BuildPath(string? registrationId, string? relativePath) =>
        CloudAccountPath.BuildPath("onedrive", registrationId, relativePath);
}