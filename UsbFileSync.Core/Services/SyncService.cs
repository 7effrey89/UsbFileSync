using System.Security.Cryptography;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Strategies;

namespace UsbFileSync.Core.Services;

public sealed class SyncService
{
    private const long OneMegabyte = 1024L * 1024;
    private const long FourMegabytes = 4 * OneMegabyte;
    private const long SixteenMegabytes = 16 * OneMegabyte;
    private const long ThirtyTwoMegabytes = 32 * OneMegabyte;
    private const long SixtyFourMegabytes = 64 * OneMegabyte;
    private const long OneHundredTwentyEightMegabytes = 128 * OneMegabyte;
    private const long TwoHundredFiftySixMegabytes = 256 * OneMegabyte;
    private const int MinimumAutoParallelismLimit = 4;
    private const int MaximumAutoParallelismLimit = 32;
    private const int FastCopyThresholdMilliseconds = 250;
    private const int SlowCopyThresholdMilliseconds = 4000;

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
        IProgress<int>? autoParallelism = null,
        CancellationToken cancellationToken = default)
    {
        var actions = await AnalyzeChangesAsync(configuration, cancellationToken).ConfigureAwait(false);
        return await ExecutePlannedAsync(configuration, actions, progress, autoParallelism, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncResult> ExecutePlannedAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress = null,
        IProgress<int>? autoParallelism = null,
        CancellationToken cancellationToken = default)
    {
        if (configuration.DryRun)
        {
            return new SyncResult(actions, 0, true);
        }

        var appliedOperations = 0;
        var verifiedChecksumsByRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trackedChecksumEntriesByRelativePath = configuration.VerifyChecksums
            ? LoadTrackedChecksumEntries(configuration)
            : new Dictionary<string, SyncFileIndexEntry>(StringComparer.OrdinalIgnoreCase);

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
                    trackedChecksumEntriesByRelativePath,
                    verifiedChecksumsByRelativePath,
                    progress,
                    autoParallelism,
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

        if (configuration.Mode is SyncMode.OneWay or SyncMode.TwoWay)
        {
            PersistSyncMetadata(configuration, actions, verifiedChecksumsByRelativePath);
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

    private static int NormalizeParallelCopyCount(int parallelCopyCount)
    {
        return Math.Max(1, parallelCopyCount);
    }

    private static async Task<int> ExecuteCopyBatchAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IReadOnlyDictionary<string, SyncFileIndexEntry> trackedChecksumEntriesByRelativePath,
        IDictionary<string, string> verifiedChecksumsByRelativePath,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        int completedOperations,
        int totalActions,
        CancellationToken cancellationToken)
    {
        var workItems = actions
            .Select(action =>
            {
                var sourcePath = action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
                    ? action.SourceFullPath!
                    : action.DestinationFullPath!;
                var destinationPath = action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
                    ? action.DestinationFullPath!
                    : action.SourceFullPath!;

                return new CopyWorkItem(
                    action,
                    sourcePath,
                    destinationPath,
                    new FileInfo(sourcePath).Length,
                    TryGetReusableSourceChecksum(action, trackedChecksumEntriesByRelativePath));
            })
            .ToArray();

        var isAutoParallelism = configuration.ParallelCopyCount == 0;
        var completedInBatch = 0;
        var completedSamples = new List<CopyExecutionSample>();
        var runningTasks = new List<Task<CopyExecutionSample>>();
        var nextWorkItemIndex = 0;
        var targetParallelism = isAutoParallelism
            ? EstimateAutoParallelism(workItems, completedSamples)
            : NormalizeParallelCopyCount(configuration.ParallelCopyCount);

        if (isAutoParallelism)
        {
            autoParallelism?.Report(targetParallelism);
        }

        while (nextWorkItemIndex < workItems.Length || runningTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (nextWorkItemIndex < workItems.Length && runningTasks.Count < targetParallelism)
            {
                var workItem = workItems[nextWorkItemIndex++];
                runningTasks.Add(CopyWorkItemAsync(
                    workItem,
                    configuration.VerifyChecksums,
                    progress,
                    () => completedOperations + Volatile.Read(ref completedInBatch),
                    totalActions,
                    cancellationToken));
            }

            if (runningTasks.Count == 0)
            {
                continue;
            }

            var completedTask = await Task.WhenAny(runningTasks).ConfigureAwait(false);
            runningTasks.Remove(completedTask);

            var sample = await completedTask.ConfigureAwait(false);
            completedSamples.Add(sample);

            if (!string.IsNullOrWhiteSpace(sample.VerifiedChecksum))
            {
                verifiedChecksumsByRelativePath[sample.Action.RelativePath] = sample.VerifiedChecksum;
            }

            var batchCompleted = Interlocked.Increment(ref completedInBatch);
            progress?.Report(new SyncProgress(completedOperations + batchCompleted, totalActions, sample.Action.RelativePath, sample.TotalBytes, sample.TotalBytes, 100));

            if (isAutoParallelism)
            {
                var adjustedParallelism = EstimateAutoParallelism(workItems[nextWorkItemIndex..], completedSamples);
                if (adjustedParallelism != targetParallelism)
                {
                    targetParallelism = adjustedParallelism;
                    autoParallelism?.Report(targetParallelism);
                }
            }
        }

        return completedInBatch;
    }

    private static async Task<CopyExecutionSample> CopyWorkItemAsync(
        CopyWorkItem workItem,
        bool verifyChecksums,
        IProgress<SyncProgress>? progress,
        Func<int> getCompletedOperations,
        int totalActions,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var verifiedChecksum = await CopyAsync(
            workItem.SourcePath,
            workItem.DestinationPath,
            verifyChecksums,
            workItem.ReusableSourceChecksum,
            progress,
            getCompletedOperations,
            totalActions,
            workItem.Action.RelativePath,
            cancellationToken).ConfigureAwait(false);

        return new CopyExecutionSample(workItem.Action, workItem.TotalBytes, DateTime.UtcNow - startedAt, verifiedChecksum);
    }

    private static int EstimateAutoParallelism(
        IReadOnlyList<CopyWorkItem> remainingItems,
        IReadOnlyList<CopyExecutionSample> completedSamples)
    {
        if (remainingItems.Count == 0)
        {
            return 1;
        }

        var averageSize = remainingItems.Average(item => (double)item.TotalBytes);
        var smallFileRatio = remainingItems.Count(item => item.TotalBytes <= OneMegabyte) / (double)remainingItems.Count;
        var largeFileRatio = remainingItems.Count(item => item.TotalBytes >= OneHundredTwentyEightMegabytes) / (double)remainingItems.Count;

        var estimatedParallelism = averageSize switch
        {
            >= TwoHundredFiftySixMegabytes => 2,
            >= SixtyFourMegabytes => 4,
            >= SixteenMegabytes => 6,
            >= FourMegabytes => 8,
            >= OneMegabyte => 12,
            _ => 16,
        };

        if (smallFileRatio >= 0.75)
        {
            estimatedParallelism += 4;
        }

        if (largeFileRatio >= 0.5)
        {
            estimatedParallelism = Math.Min(estimatedParallelism, 4);
        }

        if (completedSamples.Count >= 2)
        {
            var averageDuration = completedSamples.Average(sample => sample.Duration.TotalMilliseconds);
            if (averageDuration < FastCopyThresholdMilliseconds && averageSize <= OneMegabyte)
            {
                estimatedParallelism += 2;
            }
            else if (averageDuration > SlowCopyThresholdMilliseconds && averageSize >= ThirtyTwoMegabytes)
            {
                estimatedParallelism = Math.Max(2, estimatedParallelism - 2);
            }
        }

        var autoParallelismLimit = Math.Clamp(Environment.ProcessorCount * 2, MinimumAutoParallelismLimit, MaximumAutoParallelismLimit);
        return Math.Clamp(estimatedParallelism, 1, Math.Min(autoParallelismLimit, remainingItems.Count));
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

    private static async Task<string?> CopyAsync(
        string sourcePath,
        string destinationPath,
        bool verifyChecksums,
        string? reusableSourceChecksum,
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
        IncrementalHash? sourceHash = null;
        IncrementalHash? destinationHash = null;
        string? copiedChecksum = null;

        try
        {
            if (verifyChecksums)
            {
                if (string.IsNullOrWhiteSpace(reusableSourceChecksum))
                {
                    sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                }

                destinationHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            }

            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true))
            await using (var destinationStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sourceHash?.AppendData(buffer, 0, bytesRead);
                    await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    destinationHash?.AppendData(buffer, 0, bytesRead);
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
                var expectedSourceChecksum = string.IsNullOrWhiteSpace(reusableSourceChecksum)
                    ? Convert.ToHexString(sourceHash!.GetHashAndReset())
                    : reusableSourceChecksum;
                copiedChecksum = Convert.ToHexString(destinationHash!.GetHashAndReset());

                ValidateCopiedFileChecksums(expectedSourceChecksum, copiedChecksum, sourcePath);
            }

            CommitTemporaryCopy(tempPath, destinationPath);
            File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
            return copiedChecksum;
        }
        catch
        {
            Delete(tempPath);
            throw;
        }
        finally
        {
            sourceHash?.Dispose();
            destinationHash?.Dispose();
        }
    }

    private static void ValidateCopiedFileChecksums(string expectedSourceChecksum, string copiedChecksum, string sourcePath)
    {
        if (!string.Equals(expectedSourceChecksum, copiedChecksum, StringComparison.Ordinal))
        {
            throw new IOException($"Checksum validation failed for '{Path.GetFileName(sourcePath)}'.");
        }
    }

    private static string CreateTemporaryCopyPath(string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var destinationFileName = Path.GetFileName(destinationPath);
        return Path.Combine(destinationDirectory, $".{destinationFileName}.{Guid.NewGuid():N}.usfcopy.tmp");
    }

    private static void CommitTemporaryCopy(string temporaryPath, string destinationPath)
    {
        try
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
            File.Move(temporaryPath, destinationPath);
        }
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

    private void PersistSyncMetadata(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IReadOnlyDictionary<string, string> verifiedChecksumsByRelativePath)
    {
        var sourceMetadata = _metadataStore.Load(configuration.SourcePath);
        var destinationMetadata = _metadataStore.Load(configuration.DestinationPath);
        var sourceRootId = _metadataStore.GetOrCreateRootId(sourceMetadata);
        var destinationRootId = _metadataStore.GetOrCreateRootId(destinationMetadata);
        var sourceRootName = _metadataStore.GetRootDisplayName(configuration.SourcePath);
        var destinationRootName = _metadataStore.GetRootDisplayName(configuration.DestinationPath);
        sourceMetadata.RootName = sourceRootName;
        destinationMetadata.RootName = destinationRootName;
        var existingPeerState = _metadataStore.GetSharedPeerState(sourceMetadata, destinationRootId, destinationMetadata, sourceRootId);
        var currentPeerState = BuildCurrentPeerState(
            configuration,
            actions,
            existingPeerState,
            verifiedChecksumsByRelativePath,
            sourceRootId,
            destinationRootId,
            sourceRootName,
            destinationRootName);

        sourceMetadata.PeerStates[destinationRootId] = Clone(currentPeerState, destinationRootName);
        destinationMetadata.PeerStates[sourceRootId] = Clone(currentPeerState, sourceRootName);

        _metadataStore.Save(configuration.SourcePath, sourceMetadata);
        _metadataStore.Save(configuration.DestinationPath, destinationMetadata);
    }

    private static SyncPeerState BuildCurrentPeerState(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        SyncPeerState? existingPeerState,
        IReadOnlyDictionary<string, string> verifiedChecksumsByRelativePath,
        string sourceRootId,
        string destinationRootId,
        string sourceRootName,
        string destinationRootName)
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
                    LastSyncedByRootId = ResolveLastSyncedByRootId(action, existingEntry, sourceRootId, destinationRootId),
                    LastSyncedByRootName = ResolveLastSyncedByRootName(action, existingEntry, sourceRootId, destinationRootId, sourceRootName, destinationRootName),
                    ChecksumSha256 = ResolveChecksum(relativePath, existingEntry, sourceFile, verifiedChecksumsByRelativePath),
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
                        LastSyncedByRootId = action.Type == SyncActionType.DeleteFromDestination ? sourceRootId : destinationRootId,
                        LastSyncedByRootName = action.Type == SyncActionType.DeleteFromDestination ? sourceRootName : destinationRootName,
                    };
                }
                else if (existingEntry is not null)
                {
                    entries[relativePath] = new SyncFileIndexEntry
                    {
                        RelativePath = relativePath,
                        IsDeleted = true,
                        DeletedAtUtc = existingEntry.DeletedAtUtc ?? recordedAtUtc,
                        LastSyncedByRootId = string.IsNullOrWhiteSpace(existingEntry.LastSyncedByRootId) ? sourceRootId : existingEntry.LastSyncedByRootId,
                        LastSyncedByRootName = ResolveLastSyncedByRootName(null, existingEntry, sourceRootId, destinationRootId, sourceRootName, destinationRootName),
                    };
                }
            }
        }

        return new SyncPeerState
        {
            PeerRootName = existingPeerState?.PeerRootName ?? string.Empty,
            RecordedAtUtc = recordedAtUtc,
            Entries = entries,
        };
    }

    private static string ResolveLastSyncedByRootId(
        SyncAction? action,
        SyncFileIndexEntry? existingEntry,
        string sourceRootId,
        string destinationRootId) => action?.Type switch
    {
        SyncActionType.CopyToDestination => sourceRootId,
        SyncActionType.OverwriteFileOnDestination => sourceRootId,
        SyncActionType.CopyToSource => destinationRootId,
        SyncActionType.OverwriteFileOnSource => destinationRootId,
        _ => existingEntry is null || string.IsNullOrWhiteSpace(existingEntry.LastSyncedByRootId)
            ? sourceRootId
            : existingEntry.LastSyncedByRootId,
    };

    private static string ResolveLastSyncedByRootName(
        SyncAction? action,
        SyncFileIndexEntry? existingEntry,
        string sourceRootId,
        string destinationRootId,
        string sourceRootName,
        string destinationRootName) => action?.Type switch
    {
        SyncActionType.CopyToDestination => sourceRootName,
        SyncActionType.OverwriteFileOnDestination => sourceRootName,
        SyncActionType.CopyToSource => destinationRootName,
        SyncActionType.OverwriteFileOnSource => destinationRootName,
        SyncActionType.DeleteFromDestination => sourceRootName,
        SyncActionType.DeleteFromSource => destinationRootName,
        _ when existingEntry is not null && !string.IsNullOrWhiteSpace(existingEntry.LastSyncedByRootName) => existingEntry.LastSyncedByRootName,
        _ when existingEntry?.LastSyncedByRootId == destinationRootId => destinationRootName,
        _ => sourceRootName,
    };

    private static SyncPeerState Clone(SyncPeerState peerState, string peerRootName) => new()
    {
        PeerRootName = peerRootName,
        RecordedAtUtc = peerState.RecordedAtUtc,
        Entries = peerState.Entries.ToDictionary(
            pair => pair.Key,
            pair => Clone(pair.Value),
            StringComparer.OrdinalIgnoreCase),
    };

    private static SyncFileIndexEntry Clone(SyncFileIndexEntry entry) => new()
    {
        RelativePath = entry.RelativePath,
        Length = entry.Length,
        LastWriteTimeUtc = entry.LastWriteTimeUtc,
        IsDeleted = entry.IsDeleted,
        DeletedAtUtc = entry.DeletedAtUtc,
        LastSyncedByRootId = entry.LastSyncedByRootId,
        LastSyncedByRootName = entry.LastSyncedByRootName,
        ChecksumSha256 = entry.ChecksumSha256,
    };

    private Dictionary<string, SyncFileIndexEntry> LoadTrackedChecksumEntries(SyncConfiguration configuration)
    {
        var sourceMetadata = _metadataStore.Load(configuration.SourcePath);
        var destinationMetadata = _metadataStore.Load(configuration.DestinationPath);
        var sourceRootId = _metadataStore.GetOrCreateRootId(sourceMetadata);
        var destinationRootId = _metadataStore.GetOrCreateRootId(destinationMetadata);
        var existingPeerState = _metadataStore.GetSharedPeerState(sourceMetadata, destinationRootId, destinationMetadata, sourceRootId);
        if (existingPeerState is null)
        {
            return new Dictionary<string, SyncFileIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        return existingPeerState.Entries
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.ChecksumSha256))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryGetReusableSourceChecksum(
        SyncAction action,
        IReadOnlyDictionary<string, SyncFileIndexEntry> trackedChecksumEntriesByRelativePath)
    {
        if (!trackedChecksumEntriesByRelativePath.TryGetValue(action.RelativePath, out var trackedEntry) ||
            string.IsNullOrWhiteSpace(trackedEntry.ChecksumSha256))
        {
            return null;
        }

        var sourcePath = action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
            ? action.SourceFullPath
            : action.DestinationFullPath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var sourceSnapshot = new FileSnapshot(action.RelativePath, sourcePath, sourceInfo.Length, sourceInfo.LastWriteTimeUtc);
        return trackedEntry.Matches(sourceSnapshot)
            ? trackedEntry.ChecksumSha256
            : null;
    }

    private static string? ResolveChecksum(
        string relativePath,
        SyncFileIndexEntry? existingEntry,
        FileSnapshot currentFile,
        IReadOnlyDictionary<string, string> verifiedChecksumsByRelativePath)
    {
        if (verifiedChecksumsByRelativePath.TryGetValue(relativePath, out var verifiedChecksum))
        {
            return verifiedChecksum;
        }

        return existingEntry?.Matches(currentFile) == true
            ? existingEntry.ChecksumSha256
            : null;
    }

    private sealed record CopyWorkItem(
        SyncAction Action,
        string SourcePath,
        string DestinationPath,
        long TotalBytes,
        string? ReusableSourceChecksum);

    private sealed record CopyExecutionSample(
        SyncAction Action,
        long TotalBytes,
        TimeSpan Duration,
        string? VerifiedChecksum);
}
