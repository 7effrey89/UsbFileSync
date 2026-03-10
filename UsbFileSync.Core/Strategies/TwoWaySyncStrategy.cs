using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.Core.Strategies;

public sealed class TwoWaySyncStrategy : ISyncStrategy
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
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var relativePath in allPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasSource = sourceFiles.TryGetValue(relativePath, out var sourceFile);
            var hasDestination = destinationFiles.TryGetValue(relativePath, out var destinationFile);

            if (hasSource && hasDestination)
            {
                var currentSourceFile = sourceFile!;
                var currentDestinationFile = destinationFile!;

                if (currentSourceFile.Matches(currentDestinationFile))
                {
                    continue;
                }

                if (currentSourceFile.LastWriteTimeUtc >= currentDestinationFile.LastWriteTimeUtc)
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
                actions.Add(new SyncAction(
                    SyncActionType.CopyToDestination,
                    relativePath,
                    sourceFile!.FullPath,
                    Path.Combine(configuration.DestinationPath, relativePath)));
            }
            else if (hasDestination)
            {
                actions.Add(new SyncAction(
                    SyncActionType.CopyToSource,
                    relativePath,
                    Path.Combine(configuration.SourcePath, relativePath),
                    destinationFile!.FullPath));
            }
        }

        return Task.FromResult<IReadOnlyList<SyncAction>>(actions
            .OrderBy(action => GetActionSortRank(action.Type))
            .ThenBy(action => action.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static int GetActionSortRank(SyncActionType actionType) => actionType switch
    {
        SyncActionType.CreateDirectoryOnDestination => 0,
        SyncActionType.CreateDirectoryOnSource => 0,
        SyncActionType.CopyToDestination => 1,
        SyncActionType.CopyToSource => 1,
        SyncActionType.OverwriteFileOnDestination => 2,
        SyncActionType.OverwriteFileOnSource => 2,
        _ => 3,
    };
}
