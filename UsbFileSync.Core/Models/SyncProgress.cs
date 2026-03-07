namespace UsbFileSync.Core.Models;

public sealed record SyncProgress(int CompletedOperations, int TotalOperations, string CurrentItem);
