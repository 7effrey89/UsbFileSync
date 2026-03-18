namespace UsbFileSync.Core.Models;

public sealed record ImageRenamePlanItem(
    string SourceRelativePath,
    string SourceFullPath,
    string CurrentFileName,
    string ProposedFileName,
    string ProposedRelativePath,
    string MatchedFileNameMask,
    DateTime TimestampLocal,
    bool UsedCollisionSuffix);

public sealed record ImageRenameAnalysisResult(
    IReadOnlyList<ImageRenamePlanItem> PlannedRenames,
    int ScannedFileCount,
    int CandidateFileCount);
