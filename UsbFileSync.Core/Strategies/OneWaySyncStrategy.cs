using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Core.Strategies;

public sealed class OneWaySyncStrategy : ISyncStrategy
{
    public Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default)
    {
        DirectorySnapshotBuilder.EnsureConfigurationIsValid(configuration);
        cancellationToken.ThrowIfCancellationRequested();

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

        var sourceOnly = sourceFiles.Values.Where(file => !destinationFiles.ContainsKey(file.RelativePath)).ToList();
        var destinationOnly = destinationFiles.Values.Where(file => !sourceFiles.ContainsKey(file.RelativePath)).ToList();

        if (configuration.DetectMoves)
        {
            PairMoves(configuration, sourceOnly, destinationOnly, actions);
        }

        foreach (var file in sourceFiles.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (destinationFiles.TryGetValue(file.RelativePath, out var destinationFile))
            {
                if (!file.Matches(destinationFile))
                {
                    actions.Add(new SyncAction(
                        SyncActionType.OverwriteFileOnDestination,
                        file.RelativePath,
                        file.FullPath,
                        Path.Combine(configuration.DestinationPath, file.RelativePath)));
                }
            }
            else if (sourceOnly.Any(candidate => candidate.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                actions.Add(new SyncAction(
                    SyncActionType.CopyToDestination,
                    file.RelativePath,
                    file.FullPath,
                    Path.Combine(configuration.DestinationPath, file.RelativePath)));
            }
        }

        foreach (var file in destinationOnly)
        {
            cancellationToken.ThrowIfCancellationRequested();
            actions.Add(new SyncAction(
                SyncActionType.DeleteFromDestination,
                file.RelativePath,
                null,
                file.FullPath));
        }

        foreach (var directory in destinationDirectories
                     .Where(directory => !sourceDirectories.Contains(directory))
                     .OrderByDescending(directory => directory.Count(character => character is '\\' or '/'))
                     .ThenByDescending(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            actions.Add(new SyncAction(
                SyncActionType.DeleteDirectoryFromDestination,
                directory,
                null,
                Path.Combine(configuration.DestinationPath, directory)));
        }

        return Task.FromResult<IReadOnlyList<SyncAction>>(actions
            .OrderBy(action => GetActionSortRank(action.Type))
            .ThenBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static int GetActionSortRank(SyncActionType actionType) => actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => 0,
        SyncActionType.MoveOnDestination => 1,
        SyncActionType.CopyToDestination => 2,
        SyncActionType.OverwriteFileOnDestination => 3,
        SyncActionType.DeleteFromDestination => 4,
        SyncActionType.DeleteDirectoryFromDestination => 5,
        _ => 5,
    };

    private static void PairMoves(
        SyncConfiguration configuration,
        List<FileSnapshot> sourceOnly,
        List<FileSnapshot> destinationOnly,
        List<SyncAction> actions)
    {
        var unmatchedDestinationByFingerprint = destinationOnly
            .GroupBy(file => file.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<FileSnapshot>(group.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)), StringComparer.Ordinal);

        foreach (var sourceFile in sourceOnly.ToList())
        {
            if (!unmatchedDestinationByFingerprint.TryGetValue(sourceFile.Fingerprint, out var candidates) || candidates.Count == 0)
            {
                continue;
            }

            var destinationFile = candidates.Dequeue();
            destinationOnly.Remove(destinationFile);
            sourceOnly.Remove(sourceFile);
            actions.Add(new SyncAction(
                SyncActionType.MoveOnDestination,
                sourceFile.RelativePath,
                sourceFile.FullPath,
                Path.Combine(configuration.DestinationPath, sourceFile.RelativePath),
                destinationFile.RelativePath));
        }
    }
}
