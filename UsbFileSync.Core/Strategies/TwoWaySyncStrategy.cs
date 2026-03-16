using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Strategies;

public sealed class TwoWaySyncStrategy : ISyncStrategy
{
    private readonly SyncMetadataStore _metadataStore = new();

    public Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(
        SyncConfiguration configuration,
        CancellationToken cancellationToken = default,
        IProgress<AnalyzeProgress>? progress = null)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceVolume = configuration.ResolveSourceVolume();
        var destinationVolume = configuration.ResolveDestinationVolumes().Single();
        var sourceMetadata = _metadataStore.Load(sourceVolume);
        var destinationMetadata = _metadataStore.Load(destinationVolume);
        var sourceRootId = _metadataStore.GetOrCreateRootId(sourceMetadata);
        var destinationRootId = _metadataStore.GetOrCreateRootId(destinationMetadata);
        var peerState = _metadataStore.GetSharedPeerState(sourceMetadata, destinationRootId, destinationMetadata, sourceRootId);
        var progressTracker = progress is null ? null : new AnalyzeProgressTracker(progress, TimeSpan.FromSeconds(1));
        var sourceObserver = progressTracker?.CreateObserver(sourceVolume.Root);
        var destinationObserver = progressTracker?.CreateObserver(destinationVolume.Root);
        var sourceFiles = DirectorySnapshotBuilder.Build(sourceVolume, configuration.HideMacOsSystemFiles, configuration.ExcludedPathPatterns, sourceObserver);
        var destinationFiles = DirectorySnapshotBuilder.Build(destinationVolume, configuration.HideMacOsSystemFiles, configuration.ExcludedPathPatterns, destinationObserver);
        var sourceDirectories = DirectorySnapshotBuilder.BuildDirectories(sourceVolume, configuration.HideMacOsSystemFiles, configuration.ExcludedPathPatterns, sourceObserver);
        var destinationDirectories = DirectorySnapshotBuilder.BuildDirectories(destinationVolume, configuration.HideMacOsSystemFiles, configuration.ExcludedPathPatterns, destinationObserver);
        var actions = new List<SyncAction>();

        foreach (var directory in sourceDirectories
                     .Where(directory => !destinationDirectories.Contains(directory))
                     .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new SyncAction(
                SyncActionType.CreateDirectoryOnDestination,
                directory,
                null,
                VolumePath.CombineDisplayPath(destinationVolume, directory)));
        }

        foreach (var directory in destinationDirectories
                     .Where(directory => !sourceDirectories.Contains(directory))
                     .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new SyncAction(
                SyncActionType.CreateDirectoryOnSource,
                directory,
                VolumePath.CombineDisplayPath(sourceVolume, directory),
                null));
        }

        var allPaths = sourceFiles.Keys
            .Union(destinationFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(peerState is null ? Array.Empty<string>() : peerState.Entries.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var relativePath in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasSource = sourceFiles.TryGetValue(relativePath, out var sourceFile);
            var hasDestination = destinationFiles.TryGetValue(relativePath, out var destinationFile);
            SyncFileIndexEntry? trackedEntry = null;
            peerState?.Entries.TryGetValue(relativePath, out trackedEntry);

            if (hasSource && hasDestination)
            {
                var currentSourceFile = sourceFile!;
                var currentDestinationFile = destinationFile!;

                if (currentSourceFile.Matches(currentDestinationFile))
                {
                    continue;
                }

                var sourceMatchesTracked = trackedEntry?.Matches(currentSourceFile) == true;
                var destinationMatchesTracked = trackedEntry?.Matches(currentDestinationFile) == true;

                if (sourceMatchesTracked && !destinationMatchesTracked)
                {
                    actions.Add(new SyncAction(
                        SyncActionType.OverwriteFileOnSource,
                        relativePath,
                        currentSourceFile.FullPath,
                        currentDestinationFile.FullPath));
                }
                else if (!sourceMatchesTracked && destinationMatchesTracked)
                {
                    actions.Add(new SyncAction(
                        SyncActionType.OverwriteFileOnDestination,
                        relativePath,
                        currentSourceFile.FullPath,
                        currentDestinationFile.FullPath));
                }
                else if (currentSourceFile.LastWriteTimeUtc >= currentDestinationFile.LastWriteTimeUtc)
                {
                    actions.Add(new SyncAction(
                        SyncActionType.OverwriteFileOnDestination,
                        relativePath,
                        currentSourceFile.FullPath,
                        currentDestinationFile.FullPath));
                }
                else
                {
                    actions.Add(new SyncAction(
                        SyncActionType.OverwriteFileOnSource,
                        relativePath,
                        currentSourceFile.FullPath,
                        currentDestinationFile.FullPath));
                }

                continue;
            }

            if (hasSource)
            {
                var currentSourceFile = sourceFile!;
                actions.Add(CreateSingleSidedAction(
                    relativePath,
                    currentSourceFile,
                    trackedEntry,
                    copyActionType: SyncActionType.CopyToDestination,
                    deleteActionType: SyncActionType.DeleteFromSource,
                    existingFullPath: currentSourceFile.FullPath,
                    missingFullPath: VolumePath.CombineDisplayPath(destinationVolume, relativePath)));
            }
            else if (hasDestination)
            {
                var currentDestinationFile = destinationFile!;
                actions.Add(CreateSingleSidedAction(
                    relativePath,
                    currentDestinationFile,
                    trackedEntry,
                    copyActionType: SyncActionType.CopyToSource,
                    deleteActionType: SyncActionType.DeleteFromDestination,
                    existingFullPath: currentDestinationFile.FullPath,
                    missingFullPath: VolumePath.CombineDisplayPath(sourceVolume, relativePath)));
            }
        }

        progressTracker?.Flush();

        return Task.FromResult<IReadOnlyList<SyncAction>>(actions
            .OrderBy(action => GetActionSortRank(action.Type))
            .ThenBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static SyncAction CreateSingleSidedAction(
        string relativePath,
        FileSnapshot existingFile,
        SyncFileIndexEntry? trackedEntry,
        SyncActionType copyActionType,
        SyncActionType deleteActionType,
        string existingFullPath,
        string missingFullPath)
    {
        var shouldDelete = trackedEntry is not null &&
            !trackedEntry.IsDeleted &&
            trackedEntry.Matches(existingFile);
        var sourceFullPath = copyActionType == SyncActionType.CopyToDestination ? existingFullPath : missingFullPath;
        var destinationFullPath = copyActionType == SyncActionType.CopyToDestination ? missingFullPath : existingFullPath;

        return shouldDelete
            ? new SyncAction(deleteActionType, relativePath, sourceFullPath, destinationFullPath)
            : new SyncAction(copyActionType, relativePath, sourceFullPath, destinationFullPath);
    }

    private static int GetActionSortRank(SyncActionType actionType) => actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => 0,
        SyncActionType.CreateDirectoryOnSource => 0,
        SyncActionType.CopyToDestination => 1,
        SyncActionType.CopyToSource => 1,
        SyncActionType.OverwriteFileOnDestination => 2,
        SyncActionType.OverwriteFileOnSource => 2,
        SyncActionType.DeleteFromDestination => 3,
        SyncActionType.DeleteFromSource => 3,
        _ => 4,
    };
}
