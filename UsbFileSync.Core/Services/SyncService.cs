using System.Security.Cryptography;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Strategies;

namespace UsbFileSync.Core.Services;

public sealed class SyncService
{
    private readonly ISyncStrategy _oneWayStrategy;
    private readonly ISyncStrategy _twoWayStrategy;

    public SyncService()
        : this(new OneWaySyncStrategy(), new TwoWaySyncStrategy())
    {
    }

    public SyncService(ISyncStrategy oneWayStrategy, ISyncStrategy twoWayStrategy)
    {
        _oneWayStrategy = oneWayStrategy;
        _twoWayStrategy = twoWayStrategy;
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

                return new CopyWorkItem(action, sourcePath, new FileInfo(sourcePath).Length);
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
        await CopyAsync(
            workItem.SourcePath,
            workItem.Action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
                ? workItem.Action.DestinationFullPath!
                : workItem.Action.SourceFullPath!,
            verifyChecksums,
            progress,
            getCompletedOperations,
            totalActions,
            workItem.Action.RelativePath,
            cancellationToken).ConfigureAwait(false);

        return new CopyExecutionSample(workItem.Action, workItem.TotalBytes, DateTime.UtcNow - startedAt);
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
        var smallFileRatio = remainingItems.Count(item => item.TotalBytes <= 1 * 1024 * 1024) / (double)remainingItems.Count;
        var largeFileRatio = remainingItems.Count(item => item.TotalBytes >= 128L * 1024 * 1024) / (double)remainingItems.Count;

        var estimatedParallelism = averageSize switch
        {
            >= 256L * 1024 * 1024 => 2,
            >= 64L * 1024 * 1024 => 4,
            >= 16L * 1024 * 1024 => 6,
            >= 4L * 1024 * 1024 => 8,
            >= 1L * 1024 * 1024 => 12,
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
            if (averageDuration < 250 && averageSize <= 1 * 1024 * 1024)
            {
                estimatedParallelism += 2;
            }
            else if (averageDuration > 4000 && averageSize >= 32L * 1024 * 1024)
            {
                estimatedParallelism = Math.Max(2, estimatedParallelism - 2);
            }
        }

        var autoParallelismLimit = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
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

    private sealed record CopyWorkItem(SyncAction Action, string SourcePath, long TotalBytes);

    private sealed record CopyExecutionSample(SyncAction Action, long TotalBytes, TimeSpan Duration);
}
