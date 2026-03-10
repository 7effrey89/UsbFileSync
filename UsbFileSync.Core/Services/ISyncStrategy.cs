using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

public interface ISyncStrategy
{
    Task<IReadOnlyList<SyncAction>> AnalyzeChangesAsync(SyncConfiguration configuration, CancellationToken cancellationToken = default);
}
