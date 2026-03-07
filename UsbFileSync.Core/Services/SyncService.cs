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

    public async Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var actions = await AnalyzeChangesAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (configuration.DryRun)
        {
            return new SyncResult(actions, 0, true);
        }

        var appliedOperations = 0;
        for (var index = 0; index < actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = actions[index];
            Apply(configuration, action);
            appliedOperations++;
            progress?.Report(new SyncProgress(index + 1, actions.Count, action.RelativePath));
        }

        return new SyncResult(actions, appliedOperations, false);
    }

    private ISyncStrategy SelectStrategy(SyncConfiguration configuration) => configuration.Mode switch
    {
        SyncMode.OneWay => _oneWayStrategy,
        SyncMode.TwoWay => _twoWayStrategy,
        _ => throw new NotSupportedException($"Unsupported sync mode: {configuration.Mode}"),
    };

    private static void Apply(SyncConfiguration configuration, SyncAction action)
    {
        switch (action.Type)
        {
            case SyncActionType.CopyToDestination:
                Copy(action.SourceFullPath!, action.DestinationFullPath!);
                break;
            case SyncActionType.CopyToSource:
                Copy(action.DestinationFullPath!, action.SourceFullPath!);
                break;
            case SyncActionType.DeleteFromDestination:
                Delete(action.DestinationFullPath!);
                break;
            case SyncActionType.DeleteFromSource:
                Delete(action.SourceFullPath!);
                break;
            case SyncActionType.MoveOnDestination:
                Move(
                    Path.Combine(configuration.DestinationPath, action.PreviousRelativePath!),
                    action.DestinationFullPath!);
                break;
            case SyncActionType.NoOp:
                break;
            default:
                throw new NotSupportedException($"Unsupported sync action: {action.Type}");
        }
    }

    private static void Copy(string sourcePath, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
}
