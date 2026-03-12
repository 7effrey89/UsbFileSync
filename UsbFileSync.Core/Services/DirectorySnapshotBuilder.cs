using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

internal static class DirectorySnapshotBuilder
{
    private static readonly HashSet<string> ExcludedRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN",
        SyncMetadataStore.MetadataDirectoryName,
        "System Volume Information",
    };

    public static IReadOnlyDictionary<string, FileSnapshot> Build(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A sync path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var files = EnumerateFileSnapshots(rootPath)
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);

        return files;
    }

    public static IReadOnlySet<string> BuildDirectories(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A sync path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateDirectories(rootPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<FileSnapshot> EnumerateFileSnapshots(string rootPath)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();

            IEnumerable<string> filePaths;
            try
            {
                filePaths = Directory.EnumerateFiles(currentDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var path in filePaths)
            {
                var relativePath = Path.GetRelativePath(rootPath, path);
                if (IsExcludedRelativePath(relativePath))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                yield return new FileSnapshot(relativePath, path, info.Length, info.LastWriteTimeUtc);
            }

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var relativePath = Path.GetRelativePath(rootPath, subdirectory);
                if (IsExcludedRelativePath(relativePath))
                {
                    continue;
                }

                pendingDirectories.Push(subdirectory);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string rootPath)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                var relativePath = Path.GetRelativePath(rootPath, subdirectory);
                if (IsExcludedRelativePath(relativePath))
                {
                    continue;
                }

                yield return relativePath;
                pendingDirectories.Push(subdirectory);
            }
        }
    }

    private static bool IsExcludedRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var firstSeparatorIndex = relativePath.IndexOfAny(['\\', '/']);
        var rootSegment = firstSeparatorIndex >= 0 ? relativePath[..firstSeparatorIndex] : relativePath;
        return ExcludedRootDirectories.Contains(rootSegment);
    }

    public static void EnsureConfigurationIsValid(SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.SourcePath))
        {
            throw new ArgumentException("SourcePath is required.", nameof(configuration));
        }

        if (configuration.GetDestinationPaths().Count == 0)
        {
            throw new ArgumentException("At least one destination path is required.", nameof(configuration));
        }
    }
}
