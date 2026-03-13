using System.IO;
using System.Windows.Forms;
using System.Windows;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.App.Services;

public static class WindowsSourceLocationPickerService
{
    public static string? PickSourceLocation(string? initialPath, IFolderPickerService folderPickerService, ISourceVolumeService sourceVolumeService)
    {
        if (!OperatingSystem.IsWindows() || System.Windows.Application.Current is null)
        {
            return folderPickerService.PickFolder("Select the source drive or folder", initialPath);
        }

        var roots = GetAvailableRoots(sourceVolumeService);
        if (roots.Count == 0)
        {

            return folderPickerService.PickFolder("Select the source drive or folder", initialPath);
        }

        var dialog = new UniversalSourceLocationPickerDialog(roots, initialPath)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private static IReadOnlyList<UniversalSourceLocationPickerDialog.RootOption> GetAvailableRoots(ISourceVolumeService sourceVolumeService)
    {
        var roots = new List<UniversalSourceLocationPickerDialog.RootOption>();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rootPath = drive.Name;
            if (sourceVolumeService.TryCreateVolume(rootPath, out var macVolume, out _)
                && macVolume is not null)
            {
                roots.Add(new UniversalSourceLocationPickerDialog.RootOption(
                    rootPath,
                    $"HFS+ ({rootPath.TrimEnd(Path.DirectorySeparatorChar)})",
                    macVolume));
                continue;
            }

            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            roots.Add(new UniversalSourceLocationPickerDialog.RootOption(
                rootPath,
                BuildWindowsDisplayText(drive),
                new WindowsMountedVolume(rootPath)));
        }

        return roots;
    }

    private static string BuildWindowsDisplayText(DriveInfo drive)
    {
        var rootText = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        if (!drive.IsReady)
        {
            return $"{rootText} - {drive.DriveType}";
        }

        var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Drive" : drive.VolumeLabel;
        return $"{label} ({rootText}) - Windows";
    }
}