namespace UsbFileSync.Core.Models;

public sealed record ImageRenamePlanItem(
    string SourceRelativePath,
    string SourceFullPath,
    string CurrentFileName,
    string ProposedFileName,
    string ProposedRelativePath,
    string MatchedFileNameMask,
    DateTime TimestampLocal,
    bool UsedCollisionSuffix,
    bool IsMatchedByFileNameMask);

public sealed record ImageRenameAnalysisResult(
    IReadOnlyList<ImageRenamePlanItem> RenameSuggestions,
    int ScannedFileCount,
    int CandidateFileCount,
    int MatchedMaskCandidateCount);
