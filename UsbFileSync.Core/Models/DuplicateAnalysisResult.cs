namespace UsbFileSync.Core.Models;

public sealed record DuplicateAnalysisResult(
    IReadOnlyList<DuplicateFileGroup> Groups,
    int DuplicateGroupCount,
    int HashedFileCount);
