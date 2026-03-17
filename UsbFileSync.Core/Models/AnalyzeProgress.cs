namespace UsbFileSync.Core.Models;

public sealed record AnalyzeProgress(
    string RootPath,
    string CurrentPath,
    long FilesScanned,
    long DirectoriesScanned,
    long PendingDirectories = 0);