namespace UsbFileSync.Core.Models;

public enum DuplicateAnalyzePhase
{
    Scanning,
    Hashing,
}

public sealed record DuplicateAnalyzeProgress(
    DuplicateAnalyzePhase Phase,
    string RootPath,
    string CurrentPath,
    long FilesScanned,
    long DirectoriesScanned,
    long PendingDirectories,
    int HashedFiles,
    int TotalFilesToHash,
    int DuplicateGroupsFound);
