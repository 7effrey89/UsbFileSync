using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Core.Strategies;

public sealed class TwoWaySyncStrategy : ISyncStrategy
{
    private readonly SyncMetadataStore _metadataStore = new();

    public Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceMetadata = _metadataStore.Load(configuration.SourcePath);
        var destinationMetadata = _metadataStore.Load(configuration.DestinationPath);
        var sourceDeviceId = _metadataStore.GetOrCreateDeviceId(sourceMetadata);
        var destinationDeviceId = _metadataStore.GetOrCreateDeviceId(destinationMetadata);
        var peerState = _metadataStore.GetSharedPeerState(sourceMetadata, destinationDeviceId, destinationMetadata, sourceDeviceId);
        var sourceFiles = DirectorySnapshotBuilder.Build(configuration.SourcePath);
        var destinationFiles = DirectorySnapshotBuilder.Build(configuration.DestinationPath);
        var sourceDirectories = DirectorySnapshotBuilder.BuildDirectories(configuration.SourcePath);
        var destinationDirectories = DirectorySnapshotBuilder.BuildDirectories(configuration.DestinationPath);
        var actions = new List<SyncAction>();

        foreach (var directory in sourceDirectories
                     .Where(directory => !destinationDirectories.Contains(directory))
                     .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new SyncAction(
                SyncActionType.CreateDirectoryOnDestination,
                directory,
                null,
                Path.Combine(configuration.DestinationPath, directory)));
        }

        foreach (var directory in destinationDirectories
                     .Where(directory => !sourceDirectories.Contains(directory))
                     .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(new SyncAction(
                SyncActionType.CreateDirectoryOnSource,
                directory,
                Path.Combine(configuration.SourcePath, directory),
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
                    missingFullPath: Path.Combine(configuration.DestinationPath, relativePath)));
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
                    missingFullPath: Path.Combine(configuration.SourcePath, relativePath)));
            }
        }

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

        return shouldDelete
            ? new SyncAction(deleteActionType, relativePath, copyActionType == SyncActionType.CopyToDestination ? existingFullPath : missingFullPath, copyActionType == SyncActionType.CopyToDestination ? missingFullPath : existingFullPath)
            : new SyncAction(copyActionType, relativePath, copyActionType == SyncActionType.CopyToDestination ? existingFullPath : missingFullPath, copyActionType == SyncActionType.CopyToDestination ? missingFullPath : existingFullPath);
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
