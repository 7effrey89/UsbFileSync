namespace UsbFileSync.Platform.Windows;

internal static class CloudAccountPath
{
    public static string BuildRootPath(string scheme, string? registrationId)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
        {
            return $"{scheme}://root";
        }

        return $"{scheme}://account/{Uri.EscapeDataString(registrationId.Trim())}";
    }

    public static string BuildPath(string scheme, string? registrationId, string? relativePath)
    {
        var rootPath = BuildRootPath(scheme, registrationId);
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? rootPath
            : $"{rootPath}/{normalizedRelativePath}";
    }

    public static bool TryParse(string scheme, string? path, out string? registrationId, out string relativePath)
    {
        registrationId = null;
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var legacyRootPath = $"{scheme}://root";
        if (string.Equals(path, legacyRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWith(legacyRootPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = NormalizeRelativePath(path[(legacyRootPath.Length + 1)..]);
            return true;
        }

        var accountPrefix = $"{scheme}://account/";
        if (!path.StartsWith(accountPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = path[accountPrefix.Length..];
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var separatorIndex = remainder.IndexOf('/');
        if (separatorIndex < 0)
        {
            registrationId = Uri.UnescapeDataString(remainder);
            return true;
        }

        registrationId = Uri.UnescapeDataString(remainder[..separatorIndex]);
        relativePath = NormalizeRelativePath(remainder[(separatorIndex + 1)..]);
        return !string.IsNullOrWhiteSpace(registrationId);
    }

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');
}