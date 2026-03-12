using System.Text.Json.Serialization;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Volumes;

public static class VolumeConfigurationExtensions
{
    public static IVolumeSource ResolveSourceVolume(this SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.SourceVolume ?? new WindowsMountedVolume(configuration.SourcePath);
    }

    public static IReadOnlyList<IVolumeSource> ResolveDestinationVolumes(this SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.DestinationVolumes.Count > 0)
        {
            return configuration.DestinationVolumes;
        }

        if (configuration.DestinationVolume is not null)
        {
            return [configuration.DestinationVolume];
        }

        return configuration.GetDestinationPaths()
            .Select(path => (IVolumeSource)new WindowsMountedVolume(path))
            .ToList();
    }

    public static string ResolveSourceDisplayPath(this SyncConfiguration configuration) =>
        configuration.SourceVolume?.Root ?? configuration.SourcePath;

    public static IReadOnlyList<string> GetDestinationDisplayPaths(this SyncConfiguration configuration)
    {
        if (configuration.DestinationVolumes.Count > 0)
        {
            return configuration.DestinationVolumes.Select(volume => volume.Root).ToList();
        }

        if (configuration.DestinationVolume is not null)
        {
            return [configuration.DestinationVolume.Root];
        }

        return configuration.GetDestinationPaths();
    }
}
