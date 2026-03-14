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

    private static readonly HashSet<string> ExcludedMacOsRootEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store",
        ".DocumentRevisions-V100",
        ".fseventsd",
        ".HFS+ Private Directory Data",
        ".journal",
        ".journal_info_block",
        ".Spotlight-V100",
        ".TemporaryItems",
        ".Trashes",
        ".VolumeIcon.icns",
        "HFS+ Private Data",
    };

    public static IReadOnlyDictionary<string, FileSnapshot> Build(string rootPath) =>
        Build(new WindowsMountedVolume(rootPath));

    public static IReadOnlyDictionary<string, FileSnapshot> Build(string rootPath, bool hideMacOsSystemFiles) =>
        Build(new WindowsMountedVolume(rootPath), hideMacOsSystemFiles);

    public static IReadOnlyDictionary<string, FileSnapshot> Build(IVolumeSource volume)
        => Build(volume, hideMacOsSystemFiles: false);

    public static IReadOnlyDictionary<string, FileSnapshot> Build(IVolumeSource volume, bool hideMacOsSystemFiles)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateFileSnapshots(volume, hideMacOsSystemFiles)
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> BuildDirectories(string rootPath) =>
        BuildDirectories(new WindowsMountedVolume(rootPath));

    public static IReadOnlySet<string> BuildDirectories(string rootPath, bool hideMacOsSystemFiles) =>
        BuildDirectories(new WindowsMountedVolume(rootPath), hideMacOsSystemFiles);

    public static IReadOnlySet<string> BuildDirectories(IVolumeSource volume)
        => BuildDirectories(volume, hideMacOsSystemFiles: false);

    public static IReadOnlySet<string> BuildDirectories(IVolumeSource volume, bool hideMacOsSystemFiles)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateDirectories(volume, hideMacOsSystemFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileSnapshot> EnumerateFileSnapshots(IVolumeSource volume, bool hideMacOsSystemFiles)
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
                if (IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles) || entry.Size is null || entry.LastWriteTimeUtc is null)
                {
                    continue;
                }

                yield return new FileSnapshot(relativePath, entry.FullPath, entry.Size.Value, entry.LastWriteTimeUtc.Value);
            }

            foreach (var entry in entries.Where(entry => entry.IsDirectory))
            {
                var relativePath = GetRelativePath(volume, entry);
                if (!IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles))
                {
                    pendingDirectories.Push(relativePath);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(IVolumeSource volume, bool hideMacOsSystemFiles)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var entry in volume.Enumerate(currentDirectory).Where(entry => entry.IsDirectory))
            {
                var relativePath = GetRelativePath(volume, entry);
                if (IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles))
                {
                    continue;
                }

                yield return relativePath;
                pendingDirectories.Push(relativePath);
            }
        }
    }

    private static bool IsExcludedRelativePath(IVolumeSource volume, string relativePath, bool hideMacOsSystemFiles)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var firstSeparatorIndex = relativePath.IndexOf('/');
        var rootSegment = firstSeparatorIndex >= 0 ? relativePath[..firstSeparatorIndex] : relativePath;
        if (ExcludedRootDirectories.Contains(rootSegment))
        {
            return true;
        }

        if (!hideMacOsSystemFiles || !string.Equals(volume.FileSystemType, "HFS+", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ExcludedMacOsRootEntries.Contains(rootSegment)
            || relativePath.StartsWith("._", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/._", StringComparison.OrdinalIgnoreCase);
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
