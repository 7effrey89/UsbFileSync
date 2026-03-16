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

    public string FormatDestinationPathForDisplay(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (GoogleDrivePath.TryParse(path, out var googleRegistrationId, out var googleRelativePath))
        {
            return FormatCloudDestinationPath("gdrive", googleRegistrationId, googleRelativePath);
        }

        if (OneDrivePath.TryParse(path, out var oneDriveRegistrationId, out var oneDriveRelativePath))
        {
            return FormatCloudDestinationPath("onedrive", oneDriveRegistrationId, oneDriveRelativePath);
        }

        if (DropboxPath.TryParse(path, out var dropboxRegistrationId, out var dropboxRelativePath))
        {
            return FormatCloudDestinationPath("dropbox", dropboxRegistrationId, dropboxRelativePath);
        }

        return path;
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

    private string FormatCloudDestinationPath(string scheme, string? registrationId, string relativePath)
    {
        var alias = scheme;
        if (!string.IsNullOrWhiteSpace(registrationId))
        {
            var registration = _registrationsAccessor()
                .FirstOrDefault(item => string.Equals(item.RegistrationId, registrationId, StringComparison.OrdinalIgnoreCase));
            if (registration is not null && !string.IsNullOrWhiteSpace(registration.Alias))
            {
                alias = registration.Alias.Trim();
            }
        }

        return string.IsNullOrEmpty(relativePath)
            ? $"{scheme}://{alias}"
            : $"{scheme}://{alias}/{relativePath}";
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