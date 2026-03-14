using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Volumes;

public static class CloudPath
{
    public const string SchemePrefix = "cloud://";

    public static string CreateRoot(CloudStorageProvider provider, string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("A cloud account ID is required.", nameof(accountId));
        }

        return $"{SchemePrefix}{CloudStorageProviderInfo.GetSlug(provider)}/{accountId.Trim()}";
    }

    public static bool TryParse(
        string? path,
        out CloudStorageProvider provider,
        out string accountId,
        out string relativePath)
    {
        provider = default;
        accountId = string.Empty;
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = path[SchemePrefix.Length..]
            .Replace('\\', '/')
            .Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 ||
            !CloudStorageProviderInfo.TryParseSlug(segments[0], out provider))
        {
            return false;
        }

        accountId = segments[1];
        relativePath = segments.Length > 2
            ? VolumePath.NormalizeRelativePath(string.Join("/", segments.Skip(2)))
            : string.Empty;
        return !string.IsNullOrWhiteSpace(accountId);
    }
}
