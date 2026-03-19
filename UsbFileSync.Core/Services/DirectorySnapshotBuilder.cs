using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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

    public static IReadOnlyDictionary<string, FileSnapshot> Build(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        Action<string, bool, int>? scanObserver = null,
        bool includeSubfolders = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateFileSnapshots(volume, hideMacOsSystemFiles, excludedPathPatterns, scanObserver, includeSubfolders, cancellationToken)
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, FileSnapshot> Build(IVolumeSource volume, bool hideMacOsSystemFiles)
        => Build(volume, hideMacOsSystemFiles, excludedPathPatterns: null, scanObserver: null, includeSubfolders: true, cancellationToken: default);

    public static IReadOnlyDictionary<string, FileSnapshot> Build(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns)
        => Build(volume, hideMacOsSystemFiles, excludedPathPatterns, scanObserver: null, includeSubfolders: true, cancellationToken: default);

    public static IReadOnlySet<string> BuildDirectories(string rootPath) =>
        BuildDirectories(new WindowsMountedVolume(rootPath));

    public static IReadOnlySet<string> BuildDirectories(string rootPath, bool hideMacOsSystemFiles) =>
        BuildDirectories(new WindowsMountedVolume(rootPath), hideMacOsSystemFiles);

    public static IReadOnlySet<string> BuildDirectories(IVolumeSource volume)
        => BuildDirectories(volume, hideMacOsSystemFiles: false);

    public static IReadOnlySet<string> BuildDirectories(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        Action<string, bool, int>? scanObserver = null,
        bool includeSubfolders = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return EnumerateDirectories(volume, hideMacOsSystemFiles, excludedPathPatterns, scanObserver, includeSubfolders, cancellationToken).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlySet<string> BuildDirectories(IVolumeSource volume, bool hideMacOsSystemFiles)
        => BuildDirectories(volume, hideMacOsSystemFiles, excludedPathPatterns: null, scanObserver: null, includeSubfolders: true, cancellationToken: default);

    public static IReadOnlySet<string> BuildDirectories(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns)
        => BuildDirectories(volume, hideMacOsSystemFiles, excludedPathPatterns, scanObserver: null, includeSubfolders: true, cancellationToken: default);

    /// <summary>
    /// Scans a volume using parallel directory workers and returns both file snapshots
    /// and directory paths.  Multiple threads enumerate different directories concurrently
    /// so the OS I/O scheduler and SSD parallelism can be fully utilised.
    /// </summary>
    public static (IReadOnlyDictionary<string, FileSnapshot> Files, IReadOnlySet<string> Directories) BuildSnapshot(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        Action<string, bool, int>? scanObserver = null,
        bool includeSubfolders = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var rootEntry = volume.GetEntry(string.Empty);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            return (
                new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var fileSnapshots = new ConcurrentBag<FileSnapshot>();
        var directories = new ConcurrentBag<string>();
        var pendingWork = new ConcurrentQueue<string>();
        var outstandingWork = 1; // root directory counts as 1 item
        using var allDone = new ManualResetEventSlim(false);
        ExceptionDispatchInfo? terminalException = null;

        pendingWork.Enqueue(string.Empty);

        // Use several workers so concurrent I/O requests keep the storage device busy.
        // Capped at 4 to avoid overwhelming slow media or starving the thread-pool
        // (source + destination snapshots already run in parallel).
        var workerCount = Math.Clamp(Environment.ProcessorCount, 2, 4);
        var workers = new Thread[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = new Thread(() =>
            {
                var spinWait = new SpinWait();
                try
                {
                    while (!allDone.IsSet)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (pendingWork.TryDequeue(out var currentDirectory))
                        {
                            spinWait.Reset();
                            try
                            {
                                foreach (var entry in volume.Enumerate(currentDirectory))
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var relativePath = GetRelativePath(volume, entry);
                                    if (IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles, excludedPathPatterns))
                                    {
                                        continue;
                                    }

                                    if (entry.IsDirectory)
                                    {
                                        scanObserver?.Invoke(entry.FullPath, true, Volatile.Read(ref outstandingWork));
                                        directories.Add(relativePath);
                                        if (includeSubfolders)
                                        {
                                            Interlocked.Increment(ref outstandingWork);
                                            pendingWork.Enqueue(relativePath);
                                        }
                                    }
                                    else if (entry.Size is not null && entry.LastWriteTimeUtc is not null)
                                    {
                                        scanObserver?.Invoke(entry.FullPath, false, Volatile.Read(ref outstandingWork));
                                        fileSnapshots.Add(new FileSnapshot(
                                            relativePath, entry.FullPath, entry.Size.Value, entry.LastWriteTimeUtc.Value));
                                    }
                                }
                            }
                            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                            {
                                Interlocked.CompareExchange(ref terminalException, ExceptionDispatchInfo.Capture(exception), null);
                                allDone.Set();
                            }
                            catch (Exception)
                            {
                                // Directory enumeration failed (permissions, I/O error); skip and continue.
                            }
                            finally
                            {
                                if (Interlocked.Decrement(ref outstandingWork) == 0)
                                {
                                    allDone.Set();
                                }
                            }
                        }
                        else
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            spinWait.SpinOnce();
                        }
                    }
                        }
                        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                        {
                            Interlocked.CompareExchange(ref terminalException, ExceptionDispatchInfo.Capture(exception), null);
                            allDone.Set();
                        }
            })
            {
                IsBackground = true,
                Name = $"SnapshotWorker-{i}",
            };

            workers[i].Start();
        }

        foreach (var worker in workers)
        {
            worker.Join();
        }

        terminalException?.Throw();
        cancellationToken.ThrowIfCancellationRequested();

        var files = fileSnapshots
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);

        return (files, new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<FileSnapshot> EnumerateFileSnapshots(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        Action<string, bool, int>? scanObserver,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            var entries = volume.Enumerate(currentDirectory).ToArray();

            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = GetRelativePath(volume, entry);
                if (IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles, excludedPathPatterns) || entry.Size is null || entry.LastWriteTimeUtc is null)
                {
                    continue;
                }

                scanObserver?.Invoke(entry.FullPath, false, pendingDirectories.Count);
                yield return new FileSnapshot(relativePath, entry.FullPath, entry.Size.Value, entry.LastWriteTimeUtc.Value);
            }

            foreach (var entry in entries.Where(entry => entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = GetRelativePath(volume, entry);
                if (!IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles, excludedPathPatterns))
                {
                    if (includeSubfolders)
                    {
                        pendingDirectories.Push(relativePath);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        Action<string, bool, int>? scanObserver,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(string.Empty);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pendingDirectories.Pop();
            foreach (var entry in volume.Enumerate(currentDirectory).Where(entry => entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = GetRelativePath(volume, entry);
                if (IsExcludedRelativePath(volume, relativePath, hideMacOsSystemFiles, excludedPathPatterns))
                {
                    continue;
                }

                scanObserver?.Invoke(entry.FullPath, true, pendingDirectories.Count);
                yield return relativePath;
                if (includeSubfolders)
                {
                    pendingDirectories.Push(relativePath);
                }
            }
        }
    }

    private static bool IsExcludedRelativePath(
        IVolumeSource volume,
        string relativePath,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns)
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
            return excludedPathPatterns?.Count > 0 && MatchesExcludedPattern(relativePath, excludedPathPatterns);
        }

        return ExcludedMacOsRootEntries.Contains(rootSegment)
            || relativePath.StartsWith("._", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/._", StringComparison.OrdinalIgnoreCase)
            || (excludedPathPatterns?.Count > 0 && MatchesExcludedPattern(relativePath, excludedPathPatterns));
    }

    private static bool MatchesExcludedPattern(string relativePath, IReadOnlyList<string> excludedPathPatterns)
    {
        var pathSegments = VolumePath.NormalizeRelativePath(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var rawPattern in excludedPathPatterns)
        {
            var normalizedPattern = VolumePath.NormalizeRelativePath(rawPattern)
                .Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedPattern))
            {
                continue;
            }

            var patternSegments = normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patternSegments.Length == 0)
            {
                continue;
            }

            if (patternSegments.Length == 1)
            {
                if (pathSegments.Any(segment => WildcardMatches(segment, patternSegments[0])))
                {
                    return true;
                }

                continue;
            }

            for (var pathIndex = 0; pathIndex <= pathSegments.Length - patternSegments.Length; pathIndex++)
            {
                var matched = true;
                for (var patternIndex = 0; patternIndex < patternSegments.Length; patternIndex++)
                {
                    if (!WildcardMatches(pathSegments[pathIndex + patternIndex], patternSegments[patternIndex]))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool WildcardMatches(string input, string pattern)
    {
        var inputIndex = 0;
        var patternIndex = 0;
        var starPatternIndex = -1;
        var starInputIndex = 0;

        while (inputIndex < input.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(input[inputIndex])))
            {
                inputIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPatternIndex = patternIndex++;
                starInputIndex = inputIndex;
                continue;
            }

            if (starPatternIndex >= 0)
            {
                patternIndex = starPatternIndex + 1;
                inputIndex = ++starInputIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
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
