namespace UsbFileSync.Core.Models;

public sealed record DuplicateAnalyzeProgress(
    string CurrentPath,
    int HashedFiles,
    int TotalFilesToHash,
    int DuplicateGroupsFound);
