using System.Security.Cryptography;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Strategies;

namespace UsbFileSync.Core.Services;

public sealed class SyncService
{
    private readonly ISyncStrategy _oneWayStrategy;
    private readonly ISyncStrategy _twoWayStrategy;
    private readonly SyncMetadataStore _metadataStore;

    public SyncService()
        : this(new OneWaySyncStrategy(), new TwoWaySyncStrategy(), new SyncMetadataStore())
    {
    }

    public SyncService(ISyncStrategy oneWayStrategy, ISyncStrategy twoWayStrategy)
        : this(oneWayStrategy, twoWayStrategy, new SyncMetadataStore())
    {
    }

    private SyncService(ISyncStrategy oneWayStrategy, ISyncStrategy twoWayStrategy, SyncMetadataStore metadataStore)
    {
        _oneWayStrategy = oneWayStrategy;
        _twoWayStrategy = twoWayStrategy;
        _metadataStore = metadataStore;
    }

    public Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default) =>
        SelectStrategy(configuration).AnalyzeChangesAsync(configuration, cancellationToken);

    public async Task<IReadOnlyList<SyncPreviewItem>> BuildPreviewAsync(
        SyncConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var actions = await AnalyzeChangesAsync(configuration, cancellationToken).ConfigureAwait(false);
        return BuildPreview(configuration, actions);
    }

    public async Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var actions = await AnalyzeChangesAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ExecutePlannedAsync(configuration, actions, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncResult> ExecutePlannedAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (configuration.DryRun)
        {
            return new SyncResult(actions, 0, true);
        }

        var appliedOperations = 0;
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsCopyAction(actions[index].Type))
            {
                var copyBatch = new List<SyncAction>();
                while (index < actions.Count && IsCopyAction(actions[index].Type))
                {
                    copyBatch.Add(actions[index]);
                    index++;
                }

                appliedOperations += await ExecuteCopyBatchAsync(
                    configuration,
                    copyBatch,
                    progress,
                    appliedOperations,
                    actions.Count,
                    cancellationToken).ConfigureAwait(false);
                index--;
                continue;
            }

            var action = actions[index];
            ApplySequentialAsync(configuration, action, progress, appliedOperations, actions.Count, cancellationToken);
            appliedOperations++;
        }

        if (configuration.Mode == SyncMode.TwoWay)
        {
            PersistTwoWayMetadata(configuration, actions);
        }

        return new SyncResult(actions, appliedOperations, false);
    }

    private ISyncStrategy SelectStrategy(SyncConfiguration configuration) => configuration.Mode switch
    {
        SyncMode.OneWay => _oneWayStrategy,
        SyncMode.TwoWay => _twoWayStrategy,
        _ => throw new NotSupportedException($"Unsupported sync mode: {configuration.Mode}"),
    };

    private static IReadOnlyList<SyncPreviewItem> BuildPreview(SyncConfiguration configuration, IReadOnlyList<SyncAction> actions)
    {
        var sourceFiles = DirectorySnapshotBuilder.Build(configuration.SourcePath);
        var destinationFiles = DirectorySnapshotBuilder.Build(configuration.DestinationPath);
        var sourceDirectories = DirectorySnapshotBuilder.BuildDirectories(configuration.SourcePath);
        var destinationDirectories = DirectorySnapshotBuilder.BuildDirectories(configuration.DestinationPath);
        var actionsByRelativePath = actions
            .GroupBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var allRelativePaths = sourceFiles.Keys
            .Union(destinationFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(sourceDirectories, StringComparer.OrdinalIgnoreCase)
            .Union(destinationDirectories, StringComparer.OrdinalIgnoreCase)
            .Union(actionsByRelativePath.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return allRelativePaths
            .Select(relativePath =>
            {
                var hasSourceDirectory = sourceDirectories.Contains(relativePath);
                var hasDestinationDirectory = destinationDirectories.Contains(relativePath);
                var hasSourceFile = sourceFiles.TryGetValue(relativePath, out var sourceFile);
                var hasDestinationFile = destinationFiles.TryGetValue(relativePath, out var destinationFile);
                var isDirectory = hasSourceDirectory || hasDestinationDirectory;
                actionsByRelativePath.TryGetValue(relativePath, out var action);

                var sourceFullPath = sourceFile?.FullPath
                    ?? (hasSourceDirectory ? Path.Combine(configuration.SourcePath, relativePath) : action?.SourceFullPath);
                var destinationFullPath = destinationFile?.FullPath
                    ?? (hasDestinationDirectory ? Path.Combine(configuration.DestinationPath, relativePath) : action?.DestinationFullPath);

                var (direction, status, category) = DescribePreview(action?.Type);

                return new SyncPreviewItem(
                    relativePath,
                    isDirectory,
                    sourceFullPath,
                    sourceFile?.Length,
                    sourceFile?.LastWriteTimeUtc,
                    destinationFullPath,
                    destinationFile?.Length,
                    destinationFile?.LastWriteTimeUtc,
                    direction,
                    status,
                    category,
                    action?.Type);
            })
            .ToList();
    }

    private static (string Direction, string Status, SyncPreviewCategory Category) DescribePreview(SyncActionType? actionType) => actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => ("->", "New File", SyncPreviewCategory.NewFiles),
        SyncActionType.CopyToDestination => ("->", "New File", SyncPreviewCategory.NewFiles),
        SyncActionType.CreateDirectoryOnSource => ("<-", "New File", SyncPreviewCategory.NewFiles),
        SyncActionType.CopyToSource => ("<-", "New File", SyncPreviewCategory.NewFiles),
        SyncActionType.OverwriteFileOnDestination => ("->", "Modified", SyncPreviewCategory.ChangedFiles),
        SyncActionType.OverwriteFileOnSource => ("<-", "Modified", SyncPreviewCategory.ChangedFiles),
        SyncActionType.MoveOnDestination => ("->", "Renamed", SyncPreviewCategory.ChangedFiles),
        SyncActionType.DeleteFromDestination => ("<-", "Deleted", SyncPreviewCategory.DeletedFiles),
        SyncActionType.DeleteDirectoryFromDestination => ("<-", "Deleted", SyncPreviewCategory.DeletedFiles),
        SyncActionType.DeleteFromSource => ("->", "Deleted", SyncPreviewCategory.DeletedFiles),
        SyncActionType.DeleteDirectoryFromSource => ("->", "Deleted", SyncPreviewCategory.DeletedFiles),
        _ => ("=", "Unchanged", SyncPreviewCategory.UnchangedFiles),
    };

    private static bool IsCopyAction(SyncActionType actionType) => actionType is
        SyncActionType.CopyToDestination or
        SyncActionType.CopyToSource or
        SyncActionType.OverwriteFileOnDestination or
        SyncActionType.OverwriteFileOnSource;

    private static int NormalizeParallelCopyCount(int parallelCopyCount, int actionCount)
    {
        if (parallelCopyCount == 0)
        {
            return Math.Max(1, actionCount);
        }

        return Math.Max(1, parallelCopyCount);
    }

    private static async Task<int> ExecuteCopyBatchAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        int completedOperations,
        int totalActions,
        CancellationToken cancellationToken)
    {
        var parallelCopyCount = NormalizeParallelCopyCount(configuration.ParallelCopyCount, actions.Count);
        var semaphore = new SemaphoreSlim(parallelCopyCount, parallelCopyCount);
        var completedInBatch = 0;

        var tasks = actions.Select(async action =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                switch (action.Type)
                {
                    case SyncActionType.CopyToDestination:
                    case SyncActionType.OverwriteFileOnDestination:
                        await CopyAsync(
                            action.SourceFullPath!,
                            action.DestinationFullPath!,
                            configuration.VerifyChecksums,
                            progress,
                            () => completedOperations + Volatile.Read(ref completedInBatch),
                            totalActions,
                            action.RelativePath,
                            cancellationToken).ConfigureAwait(false);
                        break;
                    case SyncActionType.CopyToSource:
                    case SyncActionType.OverwriteFileOnSource:
                        await CopyAsync(
                            action.DestinationFullPath!,
                            action.SourceFullPath!,
                            configuration.VerifyChecksums,
                            progress,
                            () => completedOperations + Volatile.Read(ref completedInBatch),
                            totalActions,
                            action.RelativePath,
                            cancellationToken).ConfigureAwait(false);
                        break;
                }

                var batchCompleted = Interlocked.Increment(ref completedInBatch);
                var sourcePath = action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
                    ? action.SourceFullPath!
                    : action.DestinationFullPath!;
                var totalBytes = new FileInfo(sourcePath).Length;
                progress?.Report(new SyncProgress(completedOperations + batchCompleted, totalActions, action.RelativePath, totalBytes, totalBytes, 100));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return completedInBatch;
    }

    private static void ApplySequentialAsync(
        SyncConfiguration configuration,
        SyncAction action,
        IProgress<SyncProgress>? progress,
        int completedOperations,
        int totalActions,
        CancellationToken cancellationToken)
    {
        switch (action.Type)
        {
            case SyncActionType.CreateDirectoryOnDestination:
                CreateDirectory(action.DestinationFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.CreateDirectoryOnSource:
                CreateDirectory(action.SourceFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.DeleteDirectoryFromDestination:
                DeleteDirectory(action.DestinationFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.DeleteDirectoryFromSource:
                DeleteDirectory(action.SourceFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.DeleteFromDestination:
                Delete(action.DestinationFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.DeleteFromSource:
                Delete(action.SourceFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.MoveOnDestination:
                Move(
                    Path.Combine(configuration.DestinationPath, action.PreviousRelativePath!),
                    action.DestinationFullPath!);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            case SyncActionType.NoOp:
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100));
                break;
            default:
                throw new NotSupportedException($"Unsupported sync action: {action.Type}");
        }
    }

    private static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        bool verifyChecksums,
        IProgress<SyncProgress>? progress,
        Func<int> getCompletedOperations,
        int totalActions,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var tempPath = CreateTemporaryCopyPath(destinationPath);

        var totalBytes = new FileInfo(sourcePath).Length;
        progress?.Report(new SyncProgress(getCompletedOperations(), totalActions, relativePath, 0, totalBytes, totalBytes == 0 ? 100 : 0));

        const int bufferSize = 1024 * 128;
        var buffer = new byte[bufferSize];
        long transferredBytes = 0;

        try
        {
            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
            await using (var destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    transferredBytes += bytesRead;
                    var progressPercentage = totalBytes == 0
                        ? 100
                        : Math.Round((double)transferredBytes / totalBytes * 100, 0);
                    progress?.Report(new SyncProgress(getCompletedOperations(), totalActions, relativePath, transferredBytes, totalBytes, progressPercentage));
                }

                await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (verifyChecksums)
            {
                await ValidateCopiedFileChecksumsAsync(sourcePath, tempPath, cancellationToken).ConfigureAwait(false);
            }

            CommitTemporaryCopy(tempPath, destinationPath);
            File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
        }
        catch
        {
            Delete(tempPath);
            throw;
        }
    }

    private static async Task ValidateCopiedFileChecksumsAsync(string sourcePath, string copiedPath, CancellationToken cancellationToken)
    {
        var sourceHash = await ComputeChecksumAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var copiedHash = await ComputeChecksumAsync(copiedPath, cancellationToken).ConfigureAwait(false);

        if (!sourceHash.SequenceEqual(copiedHash))
        {
            throw new IOException($"Checksum validation failed for '{Path.GetFileName(sourcePath)}'.");
        }
    }

    private static async Task<byte[]> ComputeChecksumAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        using var sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTemporaryCopyPath(string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var destinationFileName = Path.GetFileName(destinationPath);
        return Path.Combine(destinationDirectory, $".{destinationFileName}.{Guid.NewGuid():N}.usfcopy.tmp");
    }

    private static void CommitTemporaryCopy(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporaryPath, destinationPath);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CreateDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path, recursive: false);
        }
    }

    private static void Move(string sourcePath, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath);
        }
    }

    private void PersistTwoWayMetadata(SyncConfiguration configuration, IReadOnlyList<SyncAction> actions)
    {
        var sourceMetadata = _metadataStore.Load(configuration.SourcePath);
        var destinationMetadata = _metadataStore.Load(configuration.DestinationPath);
        var sourceDeviceId = _metadataStore.GetOrCreateDeviceId(sourceMetadata);
        var destinationDeviceId = _metadataStore.GetOrCreateDeviceId(destinationMetadata);
        var existingPeerState = _metadataStore.GetSharedPeerState(sourceMetadata, destinationDeviceId, destinationMetadata, sourceDeviceId);
        var currentPeerState = BuildCurrentPeerState(configuration, actions, existingPeerState, sourceDeviceId, destinationDeviceId);

        sourceMetadata.PeerStates[destinationDeviceId] = currentPeerState;
        destinationMetadata.PeerStates[sourceDeviceId] = currentPeerState;

        _metadataStore.Save(configuration.SourcePath, sourceMetadata);
        _metadataStore.Save(configuration.DestinationPath, destinationMetadata);
    }

    private static SyncPeerState BuildCurrentPeerState(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        SyncPeerState? existingPeerState,
        string sourceDeviceId,
        string destinationDeviceId)
    {
        var currentSourceFiles = DirectorySnapshotBuilder.Build(configuration.SourcePath);
        var currentDestinationFiles = DirectorySnapshotBuilder.Build(configuration.DestinationPath);
        var entries = existingPeerState?.Entries.ToDictionary(
            pair => pair.Key,
            pair => Clone(pair.Value),
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, SyncFileIndexEntry>(StringComparer.OrdinalIgnoreCase);
        var actionsByRelativePath = actions
            .GroupBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var allPaths = currentSourceFiles.Keys
            .Union(currentDestinationFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(entries.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var recordedAtUtc = DateTime.UtcNow;

        foreach (var relativePath in allPaths)
        {
            var hasSource = currentSourceFiles.TryGetValue(relativePath, out var sourceFile);
            var hasDestination = currentDestinationFiles.TryGetValue(relativePath, out var destinationFile);
            actionsByRelativePath.TryGetValue(relativePath, out var action);
            entries.TryGetValue(relativePath, out var existingEntry);

            if (hasSource && hasDestination && sourceFile!.Matches(destinationFile!))
            {
                entries[relativePath] = new SyncFileIndexEntry
                {
                    RelativePath = relativePath,
                    Length = sourceFile.Length,
                    LastWriteTimeUtc = sourceFile.LastWriteTimeUtc,
                    LastSyncedBy = ResolveLastSyncedBy(action, existingEntry, sourceDeviceId, destinationDeviceId),
                };
                continue;
            }

            if (!hasSource && !hasDestination)
            {
                if (action is SyncAction { Type: SyncActionType.DeleteFromDestination or SyncActionType.DeleteFromSource })
                {
                    entries[relativePath] = new SyncFileIndexEntry
                    {
                        RelativePath = relativePath,
                        IsDeleted = true,
                        DeletedAtUtc = recordedAtUtc,
                        LastSyncedBy = action.Type == SyncActionType.DeleteFromDestination ? sourceDeviceId : destinationDeviceId,
                    };
                }
                else if (existingEntry is not null)
                {
                    entries[relativePath] = new SyncFileIndexEntry
                    {
                        RelativePath = relativePath,
                        IsDeleted = true,
                        DeletedAtUtc = existingEntry.DeletedAtUtc ?? recordedAtUtc,
                        LastSyncedBy = string.IsNullOrWhiteSpace(existingEntry.LastSyncedBy) ? sourceDeviceId : existingEntry.LastSyncedBy,
                    };
                }
            }
        }

        return new SyncPeerState
        {
            RecordedAtUtc = recordedAtUtc,
            Entries = entries,
        };
    }

    private static string ResolveLastSyncedBy(
        SyncAction? action,
        SyncFileIndexEntry? existingEntry,
        string sourceDeviceId,
        string destinationDeviceId) => action?.Type switch
    {
        SyncActionType.CopyToDestination => sourceDeviceId,
        SyncActionType.OverwriteFileOnDestination => sourceDeviceId,
        SyncActionType.CopyToSource => destinationDeviceId,
        SyncActionType.OverwriteFileOnSource => destinationDeviceId,
        _ => existingEntry is null || string.IsNullOrWhiteSpace(existingEntry.LastSyncedBy)
            ? sourceDeviceId
            : existingEntry.LastSyncedBy,
    };

    private static SyncFileIndexEntry Clone(SyncFileIndexEntry entry) => new()
    {
        RelativePath = entry.RelativePath,
        Length = entry.Length,
        LastWriteTimeUtc = entry.LastWriteTimeUtc,
        IsDeleted = entry.IsDeleted,
        DeletedAtUtc = entry.DeletedAtUtc,
        LastSyncedBy = entry.LastSyncedBy,
    };
}
