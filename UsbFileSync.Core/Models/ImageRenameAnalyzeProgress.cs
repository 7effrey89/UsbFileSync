namespace UsbFileSync.Core.Models;

public enum ImageRenameAnalyzePhase
{
    Scanning,
    Planning,
}

public sealed record ImageRenameAnalyzeProgress(
    ImageRenameAnalyzePhase Phase,
    string RootPath,
    string CurrentPath,
    long FilesScanned,
    long DirectoriesScanned,
    long PendingDirectories,
    int ProcessedFiles,
    int TotalFiles,
    int CandidateFiles,
    int PlannedRows,
    int CompletedRows);