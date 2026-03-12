namespace UsbFileSync.Core.Models;

public sealed record SyncProgress(
	int CompletedOperations,
	int TotalOperations,
	string CurrentItem,
	long CurrentItemBytesTransferred = 0,
	long? CurrentItemTotalBytes = null,
	double CurrentItemProgressPercentage = 0,
    string? CurrentItemKey = null);
