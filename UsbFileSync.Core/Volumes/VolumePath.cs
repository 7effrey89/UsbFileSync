using System.Runtime.InteropServices;

namespace UsbFileSync.Core.Volumes;

internal static class VolumePath
{
    public static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path
            .Replace('\\', '/')
            .Trim('/');
    }

    public static string CombineDisplayPath(IVolumeSource volume, string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return volume.Root;
        }

        if (LooksLikeWindowsPath(volume.Root))
        {
            var osRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(volume.Root, osRelativePath);
        }

        return $"{volume.Root.TrimEnd('/', '\\')}/{normalizedRelativePath}";
    }

    public static string GetName(string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return string.Empty;
        }

        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        return separatorIndex >= 0 ? normalizedRelativePath[(separatorIndex + 1)..] : normalizedRelativePath;
    }

    private static bool LooksLikeWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        return path.Contains('\\') || (path.Length >= 2 && path[1] == ':');
    }
}
