namespace UsbFileSync.Core.Models;

public sealed record SyncResult(IReadOnlyList<SyncAction> Actions, int AppliedOperations, bool IsDryRun);
