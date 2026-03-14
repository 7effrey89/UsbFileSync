using UsbFileSync.Core.Models;

namespace UsbFileSync.App.Services;

public interface ISyncExecutionClient
{
    Task<SyncResult> ExecuteAsync(
        SyncConfiguration configuration,
        IReadOnlyList<SyncAction> actions,
        IProgress<SyncProgress>? progress,
        IProgress<int>? autoParallelism,
        CancellationToken cancellationToken);
}