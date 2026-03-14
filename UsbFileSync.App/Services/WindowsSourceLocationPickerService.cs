using System.IO;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;
using UsbFileSync.Platform.Windows;

namespace UsbFileSync.App.Services;

public static class WindowsSourceLocationPickerService
{
    public static string? PickSourceLocation(string? initialPath, IFolderPickerService folderPickerService, ISourceVolumeService sourceVolumeService)
        => PickLocation(
            initialPath,
            folderPickerService,
            sourceVolumeService,
            fallbackTitle: "Select the source drive or folder",
            dialogTextOptions: new UniversalSourceLocationPickerDialog.DialogTextOptions(
                WindowTitle: "Select Source Folder",
                Heading: "Browse source folders across Windows, cloud, Linux ext, and HFS+ volumes",
                Description: "Select a root on the left, browse folders on the right, and choose the current folder as the source location.",
                NoRootsMessage: "No source volumes are currently available.",
                InvalidPathMessage: "Enter a valid source folder path under one of the available roots.",
                InvalidPathTitle: "Invalid source folder",
                FolderNotFoundMessage: "That folder was not found on the selected source volume.",
                FolderNotFoundTitle: "Folder not found"));

    public static string? PickDestinationLocation(string? initialPath, IFolderPickerService folderPickerService, ISourceVolumeService destinationVolumeService)
        => PickLocation(
            initialPath,
            folderPickerService,
            GetDestinationBrowseVolumeService(destinationVolumeService),
            fallbackTitle: "Select the destination drive or folder",
            dialogTextOptions: new UniversalSourceLocationPickerDialog.DialogTextOptions(
                WindowTitle: "Select Destination Folder",
                Heading: "Browse destination folders across Windows, cloud, and Linux ext volumes",
                Description: "Select a root on the left, browse folders on the right, and choose the current folder as the destination location.",
                NoRootsMessage: "No destination volumes are currently available.",
                InvalidPathMessage: "Enter a valid destination folder path under one of the available roots.",
                InvalidPathTitle: "Invalid destination folder",
                FolderNotFoundMessage: "That folder was not found on the selected destination volume.",
                FolderNotFoundTitle: "Folder not found"));

    internal static ISourceVolumeService GetDestinationBrowseVolumeService(ISourceVolumeService destinationVolumeService) =>
        GetDestinationBrowseVolumeService(destinationVolumeService, static () => new ExtVolumeService());

    internal static ISourceVolumeService GetDestinationBrowseVolumeService(
        ISourceVolumeService destinationVolumeService,
        Func<ISourceVolumeService> readOnlyExtVolumeServiceFactory)
    {
        ArgumentNullException.ThrowIfNull(destinationVolumeService);
        ArgumentNullException.ThrowIfNull(readOnlyExtVolumeServiceFactory);

        if (destinationVolumeService is ExtVolumeService)
        {
            return readOnlyExtVolumeServiceFactory();
        }

        if (destinationVolumeService is CompositeSourceVolumeService compositeVolumeService)
        {
            return new CompositeSourceVolumeService(
                compositeVolumeService.Services
                    .Select(service => service is ExtVolumeService
                        ? readOnlyExtVolumeServiceFactory()
                        : service)
                    .ToArray());
        }

        return destinationVolumeService;
    }

    private static string? PickLocation(
        string? initialPath,
        IFolderPickerService folderPickerService,
        ISourceVolumeService volumeService,
        string fallbackTitle,
        UniversalSourceLocationPickerDialog.DialogTextOptions dialogTextOptions)
    {
        if (!OperatingSystem.IsWindows() || System.Windows.Application.Current is null)
        {
            return folderPickerService.PickFolder(fallbackTitle, initialPath);
        }

        var roots = GetAvailableRoots(volumeService);
        if (roots.Count == 0)
        {
            return folderPickerService.PickFolder(fallbackTitle, initialPath);
        }

        var dialog = new UniversalSourceLocationPickerDialog(roots, initialPath, dialogTextOptions)
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private static IReadOnlyList<UniversalSourceLocationPickerDialog.RootOption> GetAvailableRoots(ISourceVolumeService volumeService)
    {
        var roots = new List<UniversalSourceLocationPickerDialog.RootOption>();
        var discoveredRootPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rootPath = drive.Name;
            discoveredRootPaths.Add(rootPath);

            if (volumeService.TryCreateVolume(rootPath, out var specialVolume, out _)
                && specialVolume is not null)
            {
                roots.Add(new UniversalSourceLocationPickerDialog.RootOption(
                    rootPath,
                    BuildSpecialVolumeDisplayText(rootPath, specialVolume),
                    specialVolume));
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

        foreach (var rootPath in EnumerateUnlistedDriveLetterRoots(discoveredRootPaths))
        {
            if (!volumeService.TryCreateVolume(rootPath, out var specialVolume, out _)
                || specialVolume is null)
            {
                continue;
            }

            roots.Add(new UniversalSourceLocationPickerDialog.RootOption(
                rootPath,
                BuildSpecialVolumeDisplayText(rootPath, specialVolume),
                specialVolume));
        }

        var cloudAccountStore = CloudAccountStoreFactory.CreateDefault();
        foreach (var account in cloudAccountStore.Load()
                     .OrderBy(account => account.Provider)
                     .ThenBy(account => account.Login, StringComparer.OrdinalIgnoreCase))
        {
            var cloudRootPath = CloudPath.CreateRoot(account.Provider, account.Id);
            if (!volumeService.TryCreateVolume(cloudRootPath, out var cloudVolume, out _)
                || cloudVolume is null)
            {
                continue;
            }

            roots.Add(new UniversalSourceLocationPickerDialog.RootOption(
                cloudRootPath,
                cloudVolume.DisplayName,
                cloudVolume));
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateUnlistedDriveLetterRoots(IReadOnlySet<string> discoveredRootPaths)
    {
        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            var rootPath = $"{letter}:{Path.DirectorySeparatorChar}";
            if (discoveredRootPaths.Contains(rootPath))
            {
                continue;
            }

            yield return rootPath;
        }
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

    private static string BuildSpecialVolumeDisplayText(string rootPath, IVolumeSource volume)
    {
        var rootText = rootPath.TrimEnd(Path.DirectorySeparatorChar);
        return volume.FileSystemType switch
        {
            "HFS+" => $"HFS+ ({rootText})",
            "ext4" => $"ext4 ({rootText})",
            _ => string.IsNullOrWhiteSpace(volume.DisplayName) ? rootText : volume.DisplayName,
        };
    }
}
