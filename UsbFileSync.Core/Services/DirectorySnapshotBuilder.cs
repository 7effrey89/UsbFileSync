using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

internal static class DirectorySnapshotBuilder
{
    private static readonly HashSet<string> ExcludedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        SyncMetadataStore.MetadataDirectoryName,
        "System Volume Information",
    };

    public static IReadOnlyDictionary<string, FileSnapshot> Build(string rootPath) =>
        Build(new WindowsMountedVolume(rootPath));

    public static IReadOnlyDictionary<string, FileSnapshot> Build(IVolumeSource volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateFileSnapshots(volume)
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> BuildDirectories(string rootPath) =>
        BuildDirectories(new WindowsMountedVolume(rootPath));

    public static IReadOnlySet<string> BuildDirectories(IVolumeSource volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateDirectories(volume).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileSnapshot> EnumerateFileSnapshots(IVolumeSource volume)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            var entries = volume.Enumerate(currentDirectory).ToArray();

            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                var relativePath = GetRelativePath(volume, entry);
                if (IsExcludedRelativePath(relativePath) || entry.Size is null || entry.LastWriteTimeUtc is null)
                {
                    continue;
                }

                yield return new FileSnapshot(relativePath, entry.FullPath, entry.Size.Value, entry.LastWriteTimeUtc.Value);
            }

            foreach (var entry in entries.Where(entry => entry.IsDirectory))
            {
                var relativePath = GetRelativePath(volume, entry);
                if (!IsExcludedRelativePath(relativePath))
                {
                    pendingDirectories.Push(relativePath);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(IVolumeSource volume)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var entry in volume.Enumerate(currentDirectory).Where(entry => entry.IsDirectory))
            {
                var relativePath = GetRelativePath(volume, entry);
                if (IsExcludedRelativePath(relativePath))
                {
                    continue;
                }

                yield return relativePath;
                pendingDirectories.Push(relativePath);
            }
        }
    }

    private static bool IsExcludedRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var firstSeparatorIndex = relativePath.IndexOf('/');
        var rootSegment = firstSeparatorIndex >= 0 ? relativePath[..firstSeparatorIndex] : relativePath;
        return ExcludedRootDirectories.Contains(rootSegment);
    }

    public static void EnsureConfigurationIsValid(SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.SourceVolume is null && string.IsNullOrWhiteSpace(configuration.SourcePath))
        {
            throw new ArgumentException("SourcePath is required.", nameof(configuration));
        }

        if (configuration.GetDestinationPaths().Count == 0 && configuration.ResolveDestinationVolumes().Count == 0)
        {
            throw new ArgumentException("At least one destination path is required.", nameof(configuration));
        }
    }

    private static string GetRelativePath(IVolumeSource volume, IFileEntry entry)
    {
        var root = volume.Root.TrimEnd('/', '\\');
        var fullPath = entry.FullPath.TrimEnd('/', '\\');
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (fullPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
        {
            return VolumePath.NormalizeRelativePath(fullPath[(root.Length + 1)..]);
        }

        return VolumePath.NormalizeRelativePath(entry.FullPath);
    }
}
