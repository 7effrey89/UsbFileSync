using System.IO;
using UsbFileSync.Core.Models;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App.Services;

public sealed class WindowsDriveDisplayNameService : IDriveDisplayNameService
{
    private readonly Func<IReadOnlyList<CloudProviderAppRegistration>> _registrationsAccessor;

    public WindowsDriveDisplayNameService(Func<IReadOnlyList<CloudProviderAppRegistration>>? registrationsAccessor = null)
    {
        _registrationsAccessor = registrationsAccessor ?? GetEmptyRegistrations;
    }

    public string FormatPathForDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (GoogleDrivePath.TryParse(path, out var googleRegistrationId, out var relativePath))
        {
            return FormatCloudPathDisplay("Google Drive", googleRegistrationId, relativePath);
        }

        if (OneDrivePath.TryParse(path, out var oneDriveRegistrationId, out relativePath))
        {
            return FormatCloudPathDisplay("OneDrive", oneDriveRegistrationId, relativePath);
        }

        if (DropboxPath.TryParse(path, out var dropboxRegistrationId, out relativePath))
        {
            return FormatCloudPathDisplay("Dropbox", dropboxRegistrationId, relativePath);
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

    private string FormatCloudPathDisplay(string providerDisplayName, string? registrationId, string relativePath)
    {
        var accountLabel = providerDisplayName;
        if (!string.IsNullOrWhiteSpace(registrationId))
        {
            var registration = _registrationsAccessor()
                .FirstOrDefault(item => string.Equals(item.RegistrationId, registrationId, StringComparison.OrdinalIgnoreCase));
            if (registration is not null && !string.IsNullOrWhiteSpace(registration.Alias))
            {
                accountLabel = $"{providerDisplayName} - {registration.Alias.Trim()}";
            }
        }

        return string.IsNullOrEmpty(relativePath)
            ? accountLabel
            : $"{accountLabel} / {relativePath}";
    }

    private static IReadOnlyList<CloudProviderAppRegistration> GetEmptyRegistrations() =>
        Array.Empty<CloudProviderAppRegistration>();

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