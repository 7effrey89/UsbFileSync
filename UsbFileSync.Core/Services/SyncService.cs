using System.Security.Cryptography;
using System.Collections.Concurrent;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Strategies;
using UsbFileSync.Core.Volumes;

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

    public async Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var actions = new List<SyncAction>();
        foreach (var destinationConfiguration in ExpandDestinationConfigurations(configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationActions = await SelectStrategy(destinationConfiguration)
                .AnalyzeChangesAsync(destinationConfiguration, cancellationToken)
                .ConfigureAwait(false);
            actions.AddRange(destinationActions);
        }

        return actions;
    }

    public async Task<IReadOnlyList<SyncPreviewItem>> BuildPreviewAsync(
        SyncConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var actions = await AnalyzeChangesAsync(configuration, cancellationToken).ConfigureAwait(false);
        return BuildPreview(configuration, actions, cancellationToken);
    }

    public IReadOnlyList<SyncPreviewItem> BuildPreview(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var previewItems = new List<SyncPreviewItem>();
        foreach (var destinationConfiguration in ExpandDestinationConfigurations(configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationActions = actions
                .Where(action => string.Equals(
                    ResolveDestinationPath(configuration, action),
                    destinationConfiguration.DestinationPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            previewItems.AddRange(BuildPreviewCore(destinationConfiguration, destinationActions, cancellationToken));
        }

        return previewItems;
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
        var totalActions = actions.Count;
        foreach (var destinationConfiguration in ExpandDestinationConfigurations(configuration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationActions = actions
                .Where(action => string.Equals(
                    ResolveDestinationPath(configuration, action),
                    destinationConfiguration.DestinationPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var result = await ExecutePlannedSingleAsync(
                destinationConfiguration,
                destinationActions,
                progress,
                autoParallelism,
                appliedOperations,
                totalActions,
                cancellationToken).ConfigureAwait(false);

            appliedOperations += result.AppliedOperations;
        }

        return new SyncResult(actions, appliedOperations, false);
    }

    private ISyncStrategy SelectStrategy(SyncConfiguration configuration) => configuration.Mode switch
    {
        SyncMode.OneWay => _oneWayStrategy,
        SyncMode.TwoWay => _twoWayStrategy,
        _ => throw new NotSupportedException($"Unsupported sync mode: {configuration.Mode}"),
    };

    private static IReadOnlyList<SyncPreviewItem> BuildPreviewCore(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        CancellationToken cancellationToken)
    {
        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();
        var sourceFiles = DirectorySnapshotBuilder.Build(sourceVolume, configuration.HideMacOsSystemFiles);
        var destinationFiles = DirectorySnapshotBuilder.Build(destinationVolume, configuration.HideMacOsSystemFiles);
        var sourceDirectories = DirectorySnapshotBuilder.BuildDirectories(sourceVolume, configuration.HideMacOsSystemFiles);
        var destinationDirectories = DirectorySnapshotBuilder.BuildDirectories(destinationVolume, configuration.HideMacOsSystemFiles);
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
                cancellationToken.ThrowIfCancellationRequested();

                var hasSourceDirectory = sourceDirectories.Contains(relativePath);
                var hasDestinationDirectory = destinationDirectories.Contains(relativePath);
                var hasSourceFile = sourceFiles.TryGetValue(relativePath, out var sourceFile);
                var hasDestinationFile = destinationFiles.TryGetValue(relativePath, out var destinationFile);
                var isDirectory = hasSourceDirectory || hasDestinationDirectory;
                actionsByRelativePath.TryGetValue(relativePath, out var action);

                var sourceFullPath = sourceFile?.FullPath
                    ?? (hasSourceDirectory ? VolumePath.CombineDisplayPath(sourceVolume, relativePath) : action?.SourceFullPath);
                var destinationFullPath = destinationFile?.FullPath
                    ?? (hasDestinationDirectory ? VolumePath.CombineDisplayPath(destinationVolume, relativePath) : action?.DestinationFullPath);

                var (direction, status, category) = DescribePreview(action?.Type);

                return new SyncPreviewItem(
                    action?.GetActionKey() ?? BuildPreviewItemKey(relativePath, sourceFullPath, destinationFullPath),
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
                        action?.Type,
                        configuration.DestinationPath);
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

    private static string BuildPreviewItemKey(string relativePath, string? sourceFullPath, string? destinationFullPath) =>
        string.Join("|", new[]
        {
            relativePath,
            sourceFullPath ?? string.Empty,
            destinationFullPath ?? string.Empty,
        });

    private static bool IsCopyAction(SyncActionType actionType) => actionType is
        SyncActionType.CopyToDestination or
        SyncActionType.CopyToSource or
        SyncActionType.OverwriteFileOnDestination or
        SyncActionType.OverwriteFileOnSource;

    private static bool RequiresCopyVerification(SyncConfiguration configuration) =>
        configuration.VerifyChecksums || configuration.MoveMode;

    private IReadOnlyList<SyncConfiguration> ExpandDestinationConfigurations(SyncConfiguration configuration)
    {
        var destinationVolumes = configuration.ResolveDestinationVolumes();
        var destinationPaths = configuration.GetDestinationPaths();
        return destinationVolumes
            .Select((destinationVolume, index) => CreateDestinationConfiguration(
                configuration,
                destinationPaths.ElementAtOrDefault(index) ?? destinationVolume.Root,
                destinationVolume))
            .ToList();
    }

    private static SyncConfiguration CreateDestinationConfiguration(
        SyncConfiguration configuration,
        string destinationPath,
        IVolumeSource destinationVolume) => new()
    {
        SourcePath = configuration.SourcePath,
        SourceVolume = configuration.SourceVolume,
        DestinationPath = destinationPath,
        DestinationVolume = destinationVolume,
        DestinationPaths = [destinationPath],
        DestinationVolumes = [destinationVolume],
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

    private static string ResolveDestinationPath(SyncConfiguration configuration, SyncAction action)
    {
        foreach (var destinationPath in configuration.GetDestinationDisplayPaths())
        {
            if (PathBelongsToRoot(action.SourceFullPath, destinationPath) ||
                PathBelongsToRoot(action.DestinationFullPath, destinationPath))
            {
                return destinationPath;
            }
        }

        return configuration.DestinationVolume?.Root ?? configuration.DestinationPath;
    }

    private static bool PathBelongsToRoot(string? candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        var normalizedRootPath = rootPath.TrimEnd('/', '\\');
        var normalizedCandidatePath = candidatePath.TrimEnd('/', '\\');
        return string.Equals(normalizedCandidatePath, normalizedRootPath, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidatePath.StartsWith(normalizedRootPath + '/', StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidatePath.StartsWith(normalizedRootPath + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SyncResult> ExecutePlannedSingleAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        int completedOperationsOffset,
        int totalActions,
        CancellationToken cancellationToken)
    {
        var appliedOperations = 0;
        var verifiedChecksumsByRelativePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trackedChecksumEntriesByRelativePath = RequiresCopyVerification(configuration)
            ? LoadTrackedChecksumEntries(configuration)
            : new Dictionary<string, SyncFileIndexEntry>(StringComparer.OrdinalIgnoreCase);
        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();

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
                    sourceVolume,
                    destinationVolume,
                    copyBatch,
                    trackedChecksumEntriesByRelativePath,
                    verifiedChecksumsByRelativePath,
                    progress,
                    autoParallelism,
                    completedOperationsOffset + appliedOperations,
                    totalActions,
                    cancellationToken).ConfigureAwait(false);
                index--;
                continue;
            }

            var action = actions[index];
            ApplySequentialAction(
                sourceVolume,
                destinationVolume,
                action,
                progress,
                completedOperationsOffset + appliedOperations,
                totalActions);
            appliedOperations++;
        }

        if (configuration.Mode is SyncMode.OneWay or SyncMode.TwoWay)
        {
            PersistSyncMetadata(configuration, actions, verifiedChecksumsByRelativePath);
        }

        return new SyncResult(actions, appliedOperations, false);
    }

    private static int NormalizeParallelCopyCount(int parallelCopyCount)
    {
        return Math.Max(1, parallelCopyCount);
    }

    private static async Task<int> ExecuteCopyBatchAsync(
        SyncConfiguration configuration,
        IVolumeSource sourceVolume,
        IVolumeSource destinationVolume,
        IReadOnlyList<SyncAction> actions,
        IReadOnlyDictionary<string, SyncFileIndexEntry> trackedChecksumEntriesByRelativePath,
        IDictionary<string, string> verifiedChecksumsByRelativePath,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        int completedOperations,
        int totalActions,
        CancellationToken cancellationToken)
    {
        var verifyCopiedFiles = RequiresCopyVerification(configuration);
        var workItems = actions
            .Select(action =>
            {
                var (copySourceVolume, copyDestinationVolume) = action.Type is SyncActionType.CopyToDestination or SyncActionType.OverwriteFileOnDestination
                    ? (sourceVolume, destinationVolume)
                    : (destinationVolume, sourceVolume);

                var sourceEntry = copySourceVolume.GetEntry(action.RelativePath);
                return new CopyWorkItem(
                    action,
                    copySourceVolume,
                    copyDestinationVolume,
                    action.RelativePath,
                    action.RelativePath,
                    sourceEntry.Size ?? 0,
                        TryGetReusableSourceChecksum(copySourceVolume, action, trackedChecksumEntriesByRelativePath),
                        configuration.MoveMode);
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
                    verifyCopiedFiles,
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
            progress?.Report(new SyncProgress(
                completedOperations + batchCompleted,
                totalActions,
                sample.Action.RelativePath,
                sample.TotalBytes,
                sample.TotalBytes,
                100,
                sample.Action.GetActionKey()));

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
            workItem.SourceVolume,
            workItem.SourceRelativePath,
            workItem.DestinationVolume,
            workItem.DestinationRelativePath,
            verifyChecksums,
            workItem.DeleteSourceAfterCopy,
            workItem.ReusableSourceChecksum,
            progress,
            getCompletedOperations,
            totalActions,
            workItem.Action.RelativePath,
            workItem.Action.GetActionKey(),
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

    private static void ApplySequentialAction(
        IVolumeSource sourceVolume,
        IVolumeSource destinationVolume,
        SyncAction action,
        IProgress<SyncProgress>? progress,
        int completedOperations,
        int totalActions)
    {
        switch (action.Type)
        {
            case SyncActionType.CreateDirectoryOnDestination:
                destinationVolume.CreateDirectory(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.CreateDirectoryOnSource:
                sourceVolume.CreateDirectory(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.DeleteDirectoryFromDestination:
                destinationVolume.DeleteDirectory(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.DeleteDirectoryFromSource:
                sourceVolume.DeleteDirectory(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.DeleteFromDestination:
                destinationVolume.DeleteFile(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.DeleteFromSource:
                sourceVolume.DeleteFile(action.RelativePath);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.MoveOnDestination:
                destinationVolume.Move(action.PreviousRelativePath!, action.RelativePath, overwrite: true);
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            case SyncActionType.NoOp:
                progress?.Report(new SyncProgress(completedOperations + 1, totalActions, action.RelativePath, 0, null, 100, action.GetActionKey()));
                break;
            default:
                throw new NotSupportedException($"Unsupported sync action: {action.Type}");
        }
    }

    private static async Task<string?> CopyAsync(
        IVolumeSource sourceVolume,
        string sourceRelativePath,
        IVolumeSource destinationVolume,
        string destinationRelativePath,
        bool verifyChecksums,
        bool deleteSourceAfterCopy,
        string? reusableSourceChecksum,
        IProgress<SyncProgress>? progress,
        Func<int> getCompletedOperations,
        int totalActions,
        string relativePath,
        string actionKey,
        CancellationToken cancellationToken)
    {
        var normalizedDestinationRelativePath = VolumePath.NormalizeRelativePath(destinationRelativePath);
        var destinationDirectory = Path.GetDirectoryName(normalizedDestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            destinationVolume.CreateDirectory(destinationDirectory.Replace(Path.DirectorySeparatorChar, '/'));
        }

        var tempRelativePath = CreateTemporaryCopyPath(normalizedDestinationRelativePath);
        var totalBytes = sourceVolume.GetEntry(sourceRelativePath).Size ?? 0;
        progress?.Report(new SyncProgress(getCompletedOperations(), totalActions, relativePath, 0, totalBytes, totalBytes == 0 ? 100 : 0, actionKey));

        const int bufferSize = 1024 * 128;
        var buffer = new byte[bufferSize];
        long transferredBytes = 0;
        IncrementalHash? sourceHash = null;
        IncrementalHash? destinationHash = null;
        string? copiedChecksum = null;
        var sourceLastWriteTimeUtc = sourceVolume.GetEntry(sourceRelativePath).LastWriteTimeUtc;

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

            await using (var sourceStream = sourceVolume.OpenRead(sourceRelativePath))
            await using (var destinationStream = destinationVolume.OpenWrite(tempRelativePath, overwrite: true))
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
                    progress?.Report(new SyncProgress(getCompletedOperations(), totalActions, relativePath, transferredBytes, totalBytes, progressPercentage, actionKey));
                }

                await destinationStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (verifyChecksums)
            {
                var expectedSourceChecksum = string.IsNullOrWhiteSpace(reusableSourceChecksum)
                    ? Convert.ToHexString(sourceHash!.GetHashAndReset())
                    : reusableSourceChecksum;
                copiedChecksum = Convert.ToHexString(destinationHash!.GetHashAndReset());

                ValidateCopiedFileChecksums(expectedSourceChecksum, copiedChecksum, sourceVolume.GetEntry(sourceRelativePath).FullPath);
            }

            destinationVolume.Move(tempRelativePath, destinationRelativePath, overwrite: true);
            if (sourceLastWriteTimeUtc.HasValue)
            {
                destinationVolume.SetLastWriteTimeUtc(destinationRelativePath, sourceLastWriteTimeUtc.Value);
            }

            if (deleteSourceAfterCopy)
            {
                sourceVolume.DeleteFile(sourceRelativePath);
            }

            return copiedChecksum;
        }
        catch
        {
            destinationVolume.DeleteFile(tempRelativePath);
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

    private static string CreateTemporaryCopyPath(string destinationRelativePath) =>
        TemporaryCopyPathBuilder.Build(destinationRelativePath);

    private void PersistSyncMetadata(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IReadOnlyDictionary<string, string> verifiedChecksumsByRelativePath)
    {
        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();
        var sourceMetadata = _metadataStore.Load(sourceVolume);
        var destinationMetadata = _metadataStore.Load(destinationVolume);
        var sourceRootId = _metadataStore.GetOrCreateRootId(sourceMetadata);
        var destinationRootId = _metadataStore.GetOrCreateRootId(destinationMetadata);
        var sourceRootName = _metadataStore.GetRootDisplayName(sourceVolume);
        var destinationRootName = _metadataStore.GetRootDisplayName(destinationVolume);
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

        _metadataStore.Save(sourceVolume, sourceMetadata);
        _metadataStore.Save(destinationVolume, destinationMetadata);
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
        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();
        var currentSourceFiles = DirectorySnapshotBuilder.Build(sourceVolume, configuration.HideMacOsSystemFiles);
        var currentDestinationFiles = DirectorySnapshotBuilder.Build(destinationVolume, configuration.HideMacOsSystemFiles);
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
        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();
        var sourceMetadata = _metadataStore.Load(sourceVolume);
        var destinationMetadata = _metadataStore.Load(destinationVolume);
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
        IVolumeSource sourceVolume,
        SyncAction action,
        IReadOnlyDictionary<string, SyncFileIndexEntry> trackedChecksumEntriesByRelativePath)
    {
        if (!trackedChecksumEntriesByRelativePath.TryGetValue(action.RelativePath, out var trackedEntry) ||
            string.IsNullOrWhiteSpace(trackedEntry.ChecksumSha256))
        {
            return null;
        }

        var sourceEntry = sourceVolume.GetEntry(action.RelativePath);
        if (!sourceEntry.Exists || sourceEntry.IsDirectory || sourceEntry.Size is null || sourceEntry.LastWriteTimeUtc is null)
        {
            return null;
        }

        var sourceSnapshot = new FileSnapshot(action.RelativePath, sourceEntry.FullPath, sourceEntry.Size.Value, sourceEntry.LastWriteTimeUtc.Value);
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
        IVolumeSource SourceVolume,
        IVolumeSource DestinationVolume,
        string SourceRelativePath,
        string DestinationRelativePath,
        long TotalBytes,
        string? ReusableSourceChecksum,
        bool DeleteSourceAfterCopy);

    private sealed record CopyExecutionSample(
        SyncAction Action,
        long TotalBytes,
        TimeSpan Duration,
        string? VerifiedChecksum);
}
