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
        var actions = new List<SyncAction>();

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
                        SyncActionType.CopyToDestination,
                        relativePath,
                        currentSourceFile.FullPath,
                        currentDestinationFile.FullPath));
                }
                else
                {
                    actions.Add(new SyncAction(
                        SyncActionType.CopyToSource,
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

        return Task.FromResult<IReadOnlyList<SyncAction>>(actions);
    }
}
