using UsbFileSync.Core.Models;
using UsbFileSync.Core.Services;

namespace UsbFileSync.App.Services;

internal sealed class InProcessSyncExecutionClient(SyncService syncService) : ISyncExecutionClient
{
    private readonly SyncService _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

    public Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(actions);

        return _syncService.ExecutePlannedAsync(configuration, actions, progress, autoParallelism, cancellationToken);
    }
}