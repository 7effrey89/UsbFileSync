namespace UsbFileSync.Platform.Windows;

public static class OneDrivePath
{
    public const string RootPath = "onedrive://root";

    public static bool IsOneDrivePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? path, out string relativePath)
    {
        relativePath = string.Empty;

        if (!IsOneDrivePath(path))
        {
            return false;
        }

        if (string.Equals(path, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path is null || path.Length <= RootPath.Length || path[RootPath.Length] != '/')
        {
            return false;
        }

        relativePath = NormalizeRelativePath(path[(RootPath.Length + 1)..]);
        return true;
    }

    public static string BuildPath(string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? RootPath
            : $"{RootPath}/{normalizedRelativePath}";
    }

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}