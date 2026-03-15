using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public static class SyncVolumeServiceFactory
{
    public static ISourceVolumeService CreateSourceVolumeService() =>
        new CompositeSourceVolumeService([new HfsPlusVolumeService(), new ExtVolumeService()]);

    public static ISourceVolumeService CreateDestinationVolumeService() =>
        new ExtVolumeService(allowWriteAccess: true);

    public static SyncConfiguration ResolveConfiguration(
        SyncConfiguration configuration,
        ISourceVolumeService? sourceVolumeService = null,
        ISourceVolumeService? destinationVolumeService = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        sourceVolumeService ??= CreateSourceVolumeService();
        destinationVolumeService ??= CreateDestinationVolumeService();

        var destinationPaths = configuration.GetDestinationPaths().ToList();
        var destinationVolumes = destinationPaths
            .Select(path => destinationVolumeService.TryCreateVolume(path, out var volume, out _)
                ? volume ?? new WindowsMountedVolume(path)
                : new WindowsMountedVolume(path))
            .ToList();

        return new SyncConfiguration
        {
            SourcePath = configuration.SourcePath,
            SourceVolume = sourceVolumeService.TryCreateVolume(configuration.SourcePath, out var sourceVolume, out _)
                ? sourceVolume
                : null,
            DestinationPath = configuration.DestinationPath,
            DestinationVolume = destinationVolumes.FirstOrDefault(),
            DestinationPaths = destinationPaths,
            DestinationVolumes = destinationVolumes,
            Mode = configuration.Mode,
            DetectMoves = configuration.DetectMoves,
            DryRun = configuration.DryRun,
            VerifyChecksums = configuration.VerifyChecksums,
            MoveMode = configuration.MoveMode,
            HideMacOsSystemFiles = configuration.HideMacOsSystemFiles,
            ParallelCopyCount = configuration.ParallelCopyCount,
            PreviewProviderMappings = new Dictionary<string, string>(configuration.PreviewProviderMappings, StringComparer.OrdinalIgnoreCase),
            CloudProviderAppRegistrations = configuration.CloudProviderAppRegistrations.ToList(),
        };
    }

    public static bool RequiresElevatedWorker(
        IReadOnlyList<string> destinationPaths,
        ISourceVolumeService? destinationVolumeService = null)
    {
        ArgumentNullException.ThrowIfNull(destinationPaths);

        destinationVolumeService ??= CreateDestinationVolumeService();
        foreach (var path in destinationPaths)
        {
            if (destinationVolumeService.TryCreateVolume(path, out var volume, out _) &&
                volume is not null &&
                string.Equals(volume.FileSystemType, "ext4", StringComparison.OrdinalIgnoreCase) &&
                volume.IsReadOnly)
            {
                return true;
            }
        }

        return false;
    }
}
