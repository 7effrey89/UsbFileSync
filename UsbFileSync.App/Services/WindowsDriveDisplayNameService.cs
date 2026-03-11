using System.IO;

namespace UsbFileSync.App.Services;

public sealed class WindowsDriveDisplayNameService : IDriveDisplayNameService
{
    public string FormatPathForDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var rootPath = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(rootPath) || !IsDriveRootPath(fullPath, rootPath))
            {
                return path;
            }

            var driveText = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var driveInfo = new DriveInfo(rootPath);
            var label = string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                ? GetDefaultLabel(driveInfo.DriveType)
                : driveInfo.VolumeLabel.Trim();

            return string.IsNullOrWhiteSpace(label)
                ? driveText
                : $"{label} ({driveText})";
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static bool IsDriveRootPath(string fullPath, string rootPath) =>
        string.Equals(
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultLabel(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "USB Drive",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "CD Drive",
        DriveType.Ram => "RAM Disk",
        _ => string.Empty,
    };
}